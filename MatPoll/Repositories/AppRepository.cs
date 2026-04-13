using MatPoll.Data;
using MatPoll.Models;
using Microsoft.EntityFrameworkCore;

namespace MatPoll.Repositories;

public class AppRepository
{
    private readonly AppDbContext _db;

    public AppRepository(AppDbContext db)
    {
        _db = db;
    }

    // ── Device ────────────────────────────────────────────────────────────────

    // Login: find device by MAC address AND IP address
    public Task<MatDeviceMst?> FindDeviceAsync(string mac, string ip)
    {
        return _db.Devices
            .FirstOrDefaultAsync(d => d.MACAddr == mac && d.IPAddr == ip);
    }

    // Refresh token: find device by DeviceID
    public Task<MatDeviceMst?> FindDeviceByIdAsync(decimal deviceId)
    {
        return _db.Devices
            .FirstOrDefaultAsync(d => d.DeviceID == deviceId);
    }

    // ── User ──────────────────────────────────────────────────────────────────

    public Task<MatUserMst?> FindUserAsync(string userId)
    {
        return _db.Users
            .FirstOrDefaultAsync(u => u.UserID == userId);
    }

    // ── CommTrn ───────────────────────────────────────────────────────────────

    // How many TrnStat=0 rows are waiting? (shown in poll response)
    public Task<int> CountPendingAsync()
    {
        return _db.CommTrns
            .Where(t => t.TrnStat == 0)
            .CountAsync();
    }

    // Fetch next bunch of TrnStat=0 rows AND mark them TrnStat=1 (dispatched)
    // bunchSize comes from appsettings.json → "BunchSize": 50
    public async Task<List<MatCommTrn>> FetchAndMarkDispatchedAsync(int bunchSize)
    {
        // Get the rows
        var rows = await _db.CommTrns
            .Where(t => t.TrnStat == 0)
            .OrderBy(t => t.TrnID)
            .Take(bunchSize)
            .ToListAsync();

        if (rows.Count == 0)
            return rows;

        // Mark them as dispatched in the same save
        foreach (var row in rows)
            row.TrnStat = 1;

        await _db.SaveChangesAsync();
        return rows;
    }

    // Get rows currently TrnStat=1 (already dispatched, waiting ACK)
    // Used to detect: client polling again before ACKing
    public Task<List<MatCommTrn>> GetDispatchedRowsAsync()
    {
        return _db.CommTrns
            .Where(t => t.TrnStat == 1)
            .OrderBy(t => t.TrnID)
            .ToListAsync();
    }

    // ACK: mark rows as TrnStat=2
    public async Task<int> MarkAcknowledgedAsync(List<decimal> trnIds)
    {
        var rows = await _db.CommTrns
            .Where(t => trnIds.Contains(t.TrnID) && t.TrnStat == 1)
            .ToListAsync();

        foreach (var row in rows)
            row.TrnStat = 2;

        await _db.SaveChangesAsync();
        return rows.Count;
    }

    // Stall recovery: TrnStat=1 rows older than X minutes → reset to 0
    public async Task ResetStalledRowsAsync(int timeoutMinutes)
    {
        var cutoff = DateTime.UtcNow.AddMinutes(-timeoutMinutes);

        var stalled = await _db.CommTrns
            .Where(t => t.TrnStat == 1 && t.CreatedAt < cutoff)
            .ToListAsync();

        foreach (var row in stalled)
        {
            var retry = (int)(row.RetryCnt ?? 0) + 1;
            row.RetryCnt = retry;

            if (retry >= 5)
                row.TrnStat = 9;   // Failed — give up after 5 tries
            else
                row.TrnStat = 0;   // Back to pending — will be sent again
        }

        if (stalled.Count > 0)
            await _db.SaveChangesAsync();
    }
}
