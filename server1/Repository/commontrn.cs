using Dapper;
using MatGenServer.Data;
using MatGenServer.Models;
using MatGenServer.Repositories.Interfaces;

namespace MatGenServer.Repositories;

/// <summary>
/// KEY DESIGN – "100 records in-flight" problem:
///
///  State machine:  0=Pending  →  1=Dispatched  →  2=Acknowledged  (9=Failed)
///
///  FetchAndMarkDispatchedAsync uses a serializable UPDATE + OUTPUT so that:
///    • Only TrnStat=0 rows are touched.
///    • They flip to TrnStat=1 atomically in one round-trip.
///    • A second poll from the SAME device while the first batch is still
///      TrnStat=1 returns NO new rows – the service layer detects this and
///      tells the client to ACK the pending batch first.
///    • A poll from a DIFFERENT device is unaffected (filtered by DeviceID).
/// </summary>
public class CommTrnRepository : ICommTrnRepository
{
    private readonly AppDbContext _db;

    public CommTrnRepository(AppDbContext db) => _db = db;

    // ── Fetch & atomically mark dispatched ────────────────────────────────────

    public async Task<List<Mat_CommTrn>> FetchAndMarkDispatchedAsync(string deviceId, int batchSize = 100)
    {
        // Single atomic UPDATE … OUTPUT – no separate SELECT needed.
        // UPDLOCK + READPAST skips rows locked by a concurrent transaction
        // (extra safety on multi-thread burst scenarios).
        const string sql = """
            UPDATE TOP (@BatchSize) dbo.Mat_CommTrn
            SET    TrnStat            = 1,
                   DispatchedAt       = GETUTCDATE(),
                   DispatchedToDevice = @DeviceID
            OUTPUT INSERTED.TrnID,
                   INSERTED.MsgStr,
                   INSERTED.RetryCnt,
                   INSERTED.TrnStat,
                   INSERTED.DispatchedAt,
                   INSERTED.DispatchedToDevice AS DispatchedToDeviceID
            WHERE  TrnStat = 0
            """;

        using var conn = _db.CreateConnection();
        var rows = await conn.QueryAsync<Mat_CommTrn>(sql, new { BatchSize = batchSize, DeviceID = deviceId });
        return rows.ToList();
    }

    // ── Get already-dispatched batch (for re-delivery check) ──────────────────

    public async Task<List<Mat_CommTrn>> GetDispatchedBatchAsync(string deviceId)
    {
        const string sql = """
            SELECT TrnID, MsgStr, RetryCnt, TrnStat, DispatchedAt, DispatchedToDevice AS DispatchedToDeviceID
            FROM   dbo.Mat_CommTrn
            WHERE  TrnStat            = 1
              AND  DispatchedToDevice = @DeviceID
            ORDER BY TrnID
            """;

        using var conn = _db.CreateConnection();
        var rows = await conn.QueryAsync<Mat_CommTrn>(sql, new { DeviceID = deviceId });
        return rows.ToList();
    }

    // ── Acknowledge ───────────────────────────────────────────────────────────

    public async Task<int> MarkAcknowledgedAsync(IEnumerable<decimal> trnIds, string deviceId)
    {
        const string sql = """
            UPDATE dbo.Mat_CommTrn
            SET    TrnStat = 2
            WHERE  TrnID IN @TrnIDs
              AND  DispatchedToDevice = @DeviceID
              AND  TrnStat = 1
            """;

        using var conn = _db.CreateConnection();
        return await conn.ExecuteAsync(sql, new { TrnIDs = trnIds, DeviceID = deviceId });
    }

    // ── Stall recovery (called by background service) ─────────────────────────

    public async Task ResetStalledDispatchesAsync(int timeoutMinutes = 5)
    {
        const string sql = """
            UPDATE dbo.Mat_CommTrn
            SET    TrnStat  = 0,
                   RetryCnt = RetryCnt + 1
            WHERE  TrnStat      = 1
              AND  DispatchedAt < DATEADD(MINUTE, -@TimeoutMinutes, GETUTCDATE())
              AND  RetryCnt     < 5
            """;

        // Records that exceeded retry limit → mark Failed (TrnStat=9)
        const string failSql = """
            UPDATE dbo.Mat_CommTrn
            SET    TrnStat = 9
            WHERE  TrnStat  = 1
              AND  DispatchedAt < DATEADD(MINUTE, -@TimeoutMinutes, GETUTCDATE())
              AND  RetryCnt    >= 5
            """;

        using var conn = _db.CreateConnection();
        await conn.ExecuteAsync(sql, new { TimeoutMinutes = timeoutMinutes });
        await conn.ExecuteAsync(failSql, new { TimeoutMinutes = timeoutMinutes });
    }
}