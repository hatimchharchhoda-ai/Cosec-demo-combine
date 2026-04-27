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

    public Task<MatDeviceMst?> FindDeviceAsync(decimal deviceType, string mac, string ip)
        => _db.Devices.AsNoTracking().FirstOrDefaultAsync(d =>
            d.DeviceType == deviceType &&
            d.MACAddr    == mac        &&
            d.IPAddr     == ip);

    public Task<MatDeviceMst?> FindDeviceByIdAsync(decimal deviceId)
        => _db.Devices.AsNoTracking().FirstOrDefaultAsync(d => d.DeviceID == deviceId);

    // ── CommTrn ───────────────────────────────────────────────────────────────

    // Count pending rows for this device
    public Task<int> CountPendingAsync(decimal deviceId, decimal deviceType)
        => _db.CommTrns.AsNoTracking()
            .CountAsync(t =>
                t.TrnStat    == 0 &&
                t.DeviceID   == deviceId &&
                t.DeviceType == deviceType);

    // Check if any rows are still dispatched (TrnStat=1) waiting for ACK
    public Task<bool> HasDispatchedRowsAsync(decimal deviceId, decimal deviceType)
        => _db.CommTrns.AsNoTracking()
            .AnyAsync(t =>
                t.TrnStat    == 1 &&
                t.DeviceID   == deviceId &&
                t.DeviceType == deviceType);

    // Get all dispatched rows for this device
    public Task<List<MatCommTrn>> GetDispatchedRowsAsync(decimal deviceId, decimal deviceType)
        => _db.CommTrns
            .Where(t =>
                t.TrnStat    == 1 &&
                t.DeviceID   == deviceId &&
                t.DeviceType == deviceType)
            .OrderBy(t => t.TrnID)
            .ToListAsync();

    // ── FETCH AND MARK DISPATCHED ─────────────────────────────────────────────
    // Fetch TrnStat=0 rows, flip to TrnStat=1, stamp DispatchedAt
    public async Task<List<MatCommTrn>> FetchAndMarkDispatchedAsync(
        decimal deviceId, decimal deviceType, int bunchSize)
    {
        // Step 1 — fetch rows by DeviceID + DeviceType
        var rows = await _db.CommTrns
            .Where(x =>
                x.TrnStat    == 0 &&
                x.DeviceID   == deviceId &&
                x.DeviceType == deviceType)
            .OrderBy(x => x.TrnID)
            .Take(bunchSize)
            .ToListAsync();

        if (rows.Count == 0) return rows;

        // Step 2 — single batch UPDATE for all rows
        var ids = rows.Select(r => r.TrnID).ToList();
        var now = DateTime.UtcNow;

        await _db.Database.ExecuteSqlRawAsync(@"
            UPDATE Mat_CommTrn
            SET TrnStat      = 1,
                RetryCnt     = ISNULL(RetryCnt, 0) + 1,
                DispatchedAt = {0}
            WHERE TrnID IN (" + string.Join(",", ids) + @")
            AND DeviceID     = {1}
            AND DeviceType   = {2}",
            now,        // {0} dispatch time
            deviceId,   // {1} security check
            deviceType);// {2} security check

        // Step 3 — sync local objects to match DB
        foreach (var row in rows)
        {
            row.TrnStat      = 1;
            row.RetryCnt     = (row.RetryCnt ?? 0) + 1;
            row.DispatchedAt = now;
        }

        return rows;
    }

    // ── ACK ───────────────────────────────────────────────────────────────────
    // Mark TrnStat=2, return AckResult with delays and mismatches
    public async Task<AckResult> MarkAcknowledgedAsync(
        List<decimal> trnIds, decimal deviceId, decimal deviceType)
    {
        var result = new AckResult();

        // Load rows: must match DeviceID + DeviceType + TrnStat=1
        var rows = await _db.CommTrns
            .Where(t =>
                trnIds.Contains(t.TrnID) &&
                t.DeviceID   == deviceId &&
                t.DeviceType == deviceType &&
                t.TrnStat    == 1)
            .ToListAsync();

        var foundIds = rows.Select(r => r.TrnID).ToHashSet();
        var now      = DateTime.UtcNow;

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

        // TrnIDs client claimed to ACK but not found/updated
        result.MismatchedIds = trnIds
            .Where(id => !foundIds.Contains(id))
            .ToList();

        result.UpdatedCount = rows.Count;

        if (rows.Count > 0)
            await _db.SaveChangesAsync();

        return result;
    }

    // ── RESTORE ───────────────────────────────────────────────────────────────
    // Reset TrnStat=1 → 0 for this device on reconnect
    public async Task<int> RestoreDispatchedAsync(decimal deviceId, decimal deviceType)
    {
        var rows = await _db.CommTrns
            .Where(t =>
                t.TrnStat    == 1 &&
                t.DeviceID   == deviceId &&
                t.DeviceType == deviceType)
            .ToListAsync();

        foreach (var row in rows)
            row.TrnStat = 0;

        await _db.SaveChangesAsync();
        return rows.Count;
    }

    // ── STALL RECOVERY ────────────────────────────────────────────────────────
    // Find rows stuck at TrnStat=1 past timeout, reset or fail them
    public async Task<List<StalledGroup>> ResetStalledRowsAsync(int timeoutMinutes)
    {
        var cutoff  = DateTime.UtcNow.AddMinutes(-timeoutMinutes);
        var stalled = await _db.CommTrns
            .Where(t => t.TrnStat == 1 && t.DispatchedAt < cutoff)
            .ToListAsync();

        if (stalled.Count == 0) return new List<StalledGroup>();

        // Group by DeviceID + DeviceType for logging
        var groups = stalled
            .GroupBy(r => new
            {
                DeviceID   = r.DeviceID   ,
                DeviceType = r.DeviceType 
            })
            .Select(g =>
            {
                var resetRows  = g.Where(r => (int)(r.RetryCnt ?? 0) < 5).ToList();
                var failedRows = g.Where(r => (int)(r.RetryCnt ?? 0) >= 50).ToList();

                foreach (var row in resetRows)
                    row.TrnStat = 0;

                foreach (var row in failedRows)
                    row.TrnStat = 9;

                return new StalledGroup
                {
                    // Show DeviceID instead of TypeMID for readability
                    DeviceID    = g.Key.DeviceID,
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

    // ── INSERT DEVICE EVENT ───────────────────────────────────────────────────
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

        _db.DeviceEvents.Add(entity);
        await _db.SaveChangesAsync();
    }
}