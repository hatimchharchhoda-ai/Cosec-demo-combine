using MatPoll.Data;
using MatPoll.Models;
using Microsoft.EntityFrameworkCore;
using MatPoll.DTOs;

namespace MatPoll.Repositories;

public class AppRepository
{
    private readonly AppDbContext _db;

    public AppRepository(AppDbContext db) => _db = db;

    // ── Device ────────────────────────────────────────────────────────────────

    public Task<MatDeviceMst?> FindDeviceAsync(decimal DeviceType, string mac, string ip)
        => _db.Devices.AsNoTracking().FirstOrDefaultAsync(d =>
            d. DeviceType ==  DeviceType &&
            d.MACAddr  == mac      &&
            d.IPAddr   == ip);

    public Task<MatDeviceMst?> FindDeviceByIdAsync(decimal deviceId)
        => _db.Devices.AsNoTracking().FirstOrDefaultAsync(d => d.DeviceID == deviceId);

    // ── CommTrn ───────────────────────────────────────────────────────────────

    public Task<int> CountPendingAsync()
        => _db.CommTrns.AsNoTracking().CountAsync(t => t.TrnStat == 0);

    public Task<bool> HasDispatchedRowsAsync(string typeMid)
        => _db.CommTrns.AsNoTracking().AnyAsync(t => t.TrnStat == 1 && t.TypeMID == typeMid);

    public Task<List<MatCommTrn>> GetDispatchedRowsAsync(string typeMid)
        => _db.CommTrns
            .Where(t => t.TrnStat == 1 && t.TypeMID == typeMid)
            .OrderBy(t => t.TrnID)
            .ToListAsync();

    // Fetch TrnStat=0 rows, flip to TrnStat=1, stamp TypeMID + DispatchedAt
    public async Task<List<MatCommTrn>> FetchAndMarkDispatchedAsync(
        string typeMid, int bunchSize)
    {
        var rows = await _db.CommTrns
            .Where(x => x.TrnStat == 0 && x.TypeMID == typeMid)
            .OrderBy(x => x.TrnID)
            .Take(bunchSize)
            .ToListAsync(); 
                if (rows.Count == 0) return rows;

        var now = DateTime.UtcNow;
        foreach (var row in rows)
        {
            row.TrnStat      = 1;
            row.TypeMID      = typeMid;
            row.RetryCnt     = (row.RetryCnt ?? 0) + 1;
            row.DispatchedAt = DateTime.UtcNow;
               // stamp dispatch time for ACK delay calc
        }

        await _db.SaveChangesAsync();
        return rows;
    }

    // ACK: mark TrnStat=2, return rich AckResult (updated count, mismatches, delays)
    public async Task<AckResult> MarkAcknowledgedAsync(
        List<decimal> trnIds, string typeMid)
    {
        var result = new AckResult();

        // Load rows that match: correct TypeMID + TrnStat=1
        var rows = await _db.CommTrns
            .Where(t => trnIds.Contains(t.TrnID) &&
                        t.TypeMID == typeMid      &&
                        t.TrnStat == 1)
            .ToListAsync();

        var foundIds = rows.Select(r => r.TrnID).ToHashSet();
        var now = DateTime.UtcNow;
       foreach (var row in rows)
      {
        row.TrnStat = 2;

        if (row.DispatchedAt.HasValue)
       {
        var delayMs = Math.Round(
            (now - row.DispatchedAt.Value).TotalMilliseconds, 2);
        result.AckDelays[row.TrnID] = delayMs;
       }
     }
        // Find TrnIDs client claimed to ACK but we couldn't find/update
        result.MismatchedIds = trnIds
            .Where(id => !foundIds.Contains(id))
            .ToList();

        result.UpdatedCount = rows.Count;

        if (rows.Count > 0)
            await _db.SaveChangesAsync();

        return result;
    }

    // RESTORE: reset TrnStat=1 → 0 for this TypeMID
    public async Task<int> RestoreDispatchedAsync(string typeMid)
    {
        var rows = await _db.CommTrns
            .Where(t => t.TrnStat == 1 && t.TypeMID == typeMid)
            .ToListAsync();

        foreach (var row in rows)
        {
            row.TrnStat      = 0;
            row.TypeMID      = null;
          
        }

        await _db.SaveChangesAsync();
        return rows.Count;
    }

    // Stall recovery — returns per-TypeMID groups for detailed logging
    public async Task<List<StalledGroup>> ResetStalledRowsAsync(int timeoutMinutes)
    {
        var cutoff   = DateTime.UtcNow.AddMinutes(-timeoutMinutes);
        var stalled  = await _db.CommTrns
            .Where(t => t.TrnStat == 1 && t.CreatedAt < cutoff)
            .ToListAsync();

        if (stalled.Count == 0) return new List<StalledGroup>();

        // Group by TypeMID for logging
        var groups = stalled
            .GroupBy(r => r.TypeMID ?? "unknown")
            .Select(g =>
            {
                var resetRows  = g.Where(r => (int)(r.RetryCnt ?? 0) < 5).ToList();
                var failedRows = g.Where(r => (int)(r.RetryCnt ?? 0) >= 5).ToList();

                foreach (var row in resetRows)
                {
                    row.TrnStat      = 0;
                    row.TypeMID      = null;
                    
                }
                foreach (var row in failedRows)
                    row.TrnStat = 9;

                return new StalledGroup
                {
                    TypeMID     = g.Key,
                    RowCount    = g.Count(),
                    MaxRetry    = g.Max(r => (int)(r.RetryCnt ?? 0)),
                    ResetCount  = resetRows.Count,
                    FailedCount = failedRows.Count
                };
            })
            .ToList();

        await _db.SaveChangesAsync();
        return groups;
    }

    // NEW: Insert a new event row from device (e.g. heartbeat, error, etc.)
// AppRepository.cs — new bulk insert method
// 

public async Task InsertDeviceEventAsync(
    DeviceEventDto dto, decimal deviceId, decimal? deviceType)
{
    var entity = new MatDeviceEvent
    {
        DeviceID   = deviceId,
        DeviceType = deviceType,
        Message    = dto.Message,
        EventSeqNo = dto.EventSeqNo,
        Timestamp  = DateTime.UtcNow
    };

    _db.DeviceEvents.Add(entity);          // single Add instead of AddRange
    await _db.SaveChangesAsync();
}

}
