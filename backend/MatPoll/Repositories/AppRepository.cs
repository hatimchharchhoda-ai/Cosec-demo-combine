using MatPoll.Data;
using MatPoll.Models;
using Microsoft.EntityFrameworkCore;
using MatPoll.DTOs;

namespace MatPoll.Repositories;

public class AppRepository
{
    private readonly MatPollDbContext _db;

    public AppRepository(MatPollDbContext db) => _db = db;

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
        Dictionary<decimal, bool> trnStatus, decimal deviceId, decimal deviceType)
    {
        var result = new AckResult();

        var trnIds = trnStatus.Keys.ToList();
        var now    = DateTime.UtcNow;

        // Load all matching rows in TrnStat = 1
        var rows = await _db.CommTrns
            .Where(t =>
                trnIds.Contains(t.TrnID) &&
                t.DeviceID   == deviceId &&
                t.DeviceType == deviceType &&
                t.TrnStat    == 1)
            .ToListAsync();

        var foundIds = rows.Select(r => r.TrnID).ToHashSet();

        foreach (var row in rows)
        {
            bool ack = trnStatus[row.TrnID];

            // TRUE -> TrnStat = 2
            // FALSE -> TrnStat = 9
            row.TrnStat = ack ? 2 : 9;

            if (row.DispatchedAt.HasValue)
            {
                var delayMs = Math.Round(
                    (now - row.DispatchedAt.Value).TotalMilliseconds, 2);

                result.AckDelays[row.TrnID] = delayMs;
            }
        }

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
                var failedRows = g.Where(r => (int)(r.RetryCnt ?? 0) >= 9).ToList();

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


    // Add this method to AppRepository.cs
// Update the method to detect reconnection:
public async Task UpdateLastSeenAsync(decimal deviceId)
{
    // check if device was offline before updating
    var device = await _db.Devices
        .FirstOrDefaultAsync(d => d.DeviceID == deviceId);
     
    var wasOffline = device?.IsOnline == false;
    var offlineSince = device?.OfflineSince;

    await _db.Database.ExecuteSqlRawAsync(@"
        UPDATE Mat_DeviceMst
        SET LastSeenAt   = {0},
            IsOnline     = 1,
            OfflineSince = NULL
        WHERE DeviceID   = {1}",
        DateTime.UtcNow, deviceId);

    // if device was offline, log it came back
    if (wasOffline && device != null)
        {
            
        }
}
// Add this for background job
public async Task<List<MatDeviceMst>> GetStaleDevicesAsync(int timeoutMinutes)
{
    var cutoff = DateTime.UtcNow.AddMinutes(-timeoutMinutes);
    return await _db.Devices
        .Where(d => d.IsOnline == true &&
                    d.LastSeenAt != null &&
                    d.LastSeenAt < cutoff)
        .ToListAsync();
}

public async Task MarkDevicesOfflineAsync(List<decimal> deviceIds)
{
    var now = DateTime.UtcNow;
    await _db.Database.ExecuteSqlRawAsync(@"
        UPDATE Mat_DeviceMst
        SET IsOnline     = 0,
            OfflineSince = {0}
        WHERE DeviceID IN (" + string.Join(",", deviceIds) + @")
        AND IsOnline = 1",
        now);
}

// Add this method to fetch active devices for CommTrn creation
public Task<List<MatDeviceMst>> GetActiveDevicesAsync()
    => _db.Devices
        .AsNoTracking()
        .Where(d => d.IsActive == 1)
        .ToListAsync();





       //add data in devise 


    public async Task<int> CreateCommTrnRowsAsync(
        decimal deviceId, decimal deviceType, int count)
    {
        var now = DateTime.UtcNow;
 
        // get total rows ever created for this device
        // used to continue sequence number from where we left off
        // var lastSeq = await _db.CommTrns
        //     .CountAsync(t => t.DeviceID == deviceId);
 
        // build all rows in memory first
        var rows = Enumerable.Range(1, count)
            .Select(i => new MatCommTrn
            {
                MsgStr     = $"ENROLL|UID:USR|DID:{(int)deviceId}",
                RetryCnt   = 0,
                TrnStat    = 0,           // pending
                CreatedAt  = now,
               
                DeviceID   = deviceId,
                DeviceType = deviceType
            })
            .ToList();
 
        // single AddRange + SaveChanges = ONE round trip for all rows
        _db.CommTrns.AddRange(rows);
        await _db.SaveChangesAsync();
 
        return rows.Count;
    }


}
