using MatPoll.Data;
using MatPoll.Models;
using Microsoft.EntityFrameworkCore;

namespace MatPoll.Repositories;

public class AppRepository
{
    private readonly AppDbContext _db;

    public AppRepository(AppDbContext db) => _db = db;

    // ── Device ────────────────────────────────────────────────────────────────

    public Task<MatDeviceMst?> FindDeviceAsync(decimal deviceId, string mac, string ip)
        => _db.Devices.FirstOrDefaultAsync(d =>
            d.DeviceID == deviceId &&
            d.MACAddr  == mac      &&
            d.IPAddr   == ip);

    public Task<MatDeviceMst?> FindDeviceByIdAsync(decimal deviceId)
        => _db.Devices.FirstOrDefaultAsync(d => d.DeviceID == deviceId);

    // ── CommTrn ───────────────────────────────────────────────────────────────

    // Count how many TrnStat=0 rows are waiting (for all devices)
    public Task<int> CountPendingAsync()
        => _db.CommTrns.CountAsync(t => t.TrnStat == 0);

    // Check: does this device (TypeMID) have un-ACKed rows (TrnStat=1)?
    // THIS replaces the BatchCache — TrnStat=1 in DB IS the lock
    public Task<bool> HasDispatchedRowsAsync(string typeMid)
        => _db.CommTrns.AnyAsync(t => t.TrnStat == 1 && t.TypeMID == typeMid);

    // Get dispatched rows for this device (for re-sending on reconnect)
    public Task<List<MatCommTrn>> GetDispatchedRowsAsync(string typeMid)
        => _db.CommTrns
            .Where(t => t.TrnStat == 1 && t.TypeMID == typeMid)
            .OrderBy(t => t.TrnID)
            .ToListAsync();

    // Fetch next bunch of TrnStat=0 rows and mark them TrnStat=1 with TypeMID
    public async Task<List<MatCommTrn>> FetchAndMarkDispatchedAsync(string typeMid, int bunchSize)
    {
        var rows = await _db.CommTrns
            .Where(t => t.TrnStat == 0)
            .OrderBy(t => t.TrnID)
            .Take(bunchSize)
            .ToListAsync();

        if (rows.Count == 0) return rows;

        foreach (var row in rows)
        {
            row.TrnStat  = 1;
            row.TypeMID  = typeMid;          // stamp device fingerprint
            row.RetryCnt = (row.RetryCnt ?? 0) + 1;
        }

        await _db.SaveChangesAsync();
        return rows;
    }

    // ACK: mark TrnStat=2 for rows belonging to this TypeMID
    public async Task<int> MarkAcknowledgedAsync(List<decimal> trnIds, string typeMid)
    {
        var rows = await _db.CommTrns
            .Where(t => trnIds.Contains(t.TrnID) &&
                        t.TypeMID == typeMid      &&
                        t.TrnStat == 1)
            .ToListAsync();

        foreach (var row in rows)
            row.TrnStat = 2;

        await _db.SaveChangesAsync();
        return rows.Count;
    }

    // RESTORE: reset all TrnStat=1 rows for this TypeMID back to TrnStat=0
    // Used when device reconnects or user clicks Restore button
    public async Task<int> RestoreDispatchedAsync(string typeMid)
    {
        var rows = await _db.CommTrns
            .Where(t => t.TrnStat == 1 && t.TypeMID == typeMid)
            .ToListAsync();

        foreach (var row in rows)
        {
            row.TrnStat = 0;
            row.TypeMID = null;   // clear so they can be picked up fresh
        }

        await _db.SaveChangesAsync();
        return rows.Count;
    }

    // Stall recovery: rows stuck at TrnStat=1 too long → reset back to 0
    public async Task ResetStalledRowsAsync(int timeoutMinutes)
    {
        var cutoff = DateTime.UtcNow.AddMinutes(-timeoutMinutes);

        var stalled = await _db.CommTrns
            .Where(t => t.TrnStat == 1 && t.CreatedAt < cutoff)
            .ToListAsync();

        foreach (var row in stalled)
        {
            var retry = (int)(row.RetryCnt ?? 0);
            if (retry >= 5)
                row.TrnStat = 9;
            else
            {
                row.TrnStat = 0;
                row.TypeMID = null;
            }
        }

        if (stalled.Count > 0)
            await _db.SaveChangesAsync();
    }
}
