using Serilog;

namespace MatPoll.Services;

/// <summary>
/// Structured activity logger.
/// Output format (pipe-delimited, no JSON):
///   2026-04-15 10:48:22 | LOGIN SUCCESS    | TypeMID=abc123
///   2026-04-15 10:48:22 | RESTORE called   | TypeMID=abc123
///   2026-04-15 10:48:22 | DATA RECEIVED    | TypeMID=abc123 | 40087,40088,40089
///   2026-04-15 10:48:24 | ACK SENT         | TypeMID=abc123
/// </summary>
public class ActivityLogger
{
    private static string Now() =>
        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

    // ── LOGIN ─────────────────────────────────────────────────────────────────
    public void LogLogin(string typeMid, decimal deviceId, string deviceName,
        bool success, string failReason, long ms)
    {
        if (success)
            Log.Information("{Time} | LOGIN SUCCESS    | TypeMID={TypeMID} | Device={DeviceName} ({DeviceID}) | {Ms}ms",
                Now(), typeMid, deviceName, deviceId, ms);
        else
            Log.Warning("{Time} | LOGIN FAILED     | TypeMID={TypeMID} | Device={DeviceID} | Reason={Reason} | {Ms}ms",
                Now(), typeMid, deviceId, failReason, ms);
    }

    // ── REFRESH ───────────────────────────────────────────────────────────────
    public void LogRefresh(string typeMid, decimal deviceId, bool success, long ms)
    {
        if (success)
            Log.Information("{Time} | TOKEN REFRESHED  | TypeMID={TypeMID} | Device={DeviceID} | {Ms}ms",
                Now(), typeMid, deviceId, ms);
        else
            Log.Warning("{Time} | REFRESH FAILED   | TypeMID={TypeMID} | Device={DeviceID} | {Ms}ms",
                Now(), typeMid, deviceId, ms);
    }

    // ── POLL (DATA RECEIVED / NO DATA) ────────────────────────────────────────
    public void LogPoll(string typeMid, decimal deviceId,
        bool hasData, bool needAckFirst,
        int rowsSent, int totalPending,
        DateTime reqTime, long ms)
    {
        if (needAckFirst)
        {
            Log.Information("{Time} | NEED ACK FIRST   | TypeMID={TypeMID} | Pending={Pending} | {Ms}ms",
                Now(), typeMid, totalPending, ms);
        }
        else if (hasData)
        {
            // DATA RECEIVED is logged from PollController with TrnIDs — see LogPollWithIds
            // This overload used when caller doesn't have IDs (legacy path)
            Log.Information("{Time} | DATA RECEIVED    | TypeMID={TypeMID} | Rows={Rows} | Pending={Pending} | {Ms}ms",
                Now(), typeMid, rowsSent, totalPending, ms);
        }
        else
        {
            Log.Information("{Time} | NO DATA          | TypeMID={TypeMID} | Pending={Pending} | {Ms}ms",
                Now(), typeMid, totalPending, ms);
        }
    }

    // ── POLL with TrnIDs (clean client-style log) ─────────────────────────────
    public void LogPollWithIds(string typeMid, decimal deviceId,
        List<decimal> trnIds, int totalPending, long ms)
    {
        var ids = string.Join(",", trnIds);
        Log.Information("{Time} | DATA RECEIVED    | TypeMID={TypeMID} | {TrnIDs}",
            Now(), typeMid, ids);
    }

    // ── ACK ───────────────────────────────────────────────────────────────────
    public void LogAck(string typeMid, decimal deviceId,
        List<decimal> trnIds, int updatedCount,
        bool success, DateTime reqTime, long ms)
    {
        LogAck(typeMid, deviceId, trnIds, updatedCount, success, null, reqTime, ms);
    }

    // ACK overload that accepts optional Message + Header from client
    public void LogAck(string typeMid, decimal deviceId,
        List<decimal> trnIds, int updatedCount,
        bool success,
        string? message,
        DateTime reqTime, long ms)
    {
        if (success)
        {
            // Base log line — matches the example format exactly
            Log.Information("{Time} | ACK SENT         | TypeMID={TypeMID} | Rows={Rows} | {Ms}ms",
                Now(), typeMid, updatedCount, ms);

            // If client sent Message / Header, log them as extra detail lines
            // if (!string.IsNullOrWhiteSpace(header))
            //     Log.Information("{Time} | ACK HEADER       | TypeMID={TypeMID} | {Header}",
            //         Now(), typeMid, header);

            if (!string.IsNullOrWhiteSpace(message))
                Log.Information("{Time} | ACK MESSAGE      | TypeMID={TypeMID} | {Message}",
                    Now(), typeMid, message);
        }
        else
        {
            Log.Warning("{Time} | ACK FAILED       | TypeMID={TypeMID} | {Ms}ms",
                Now(), typeMid, ms);
        }
    }

    // ── RESTORE ───────────────────────────────────────────────────────────────
    public void LogRestore(string typeMid, decimal deviceId,
        int restoredCount, DateTime reqTime, long ms)
    {
        Log.Information("{Time} | RESTORE called   | TypeMID={TypeMID} | Rows={Rows} | {Ms}ms",
            Now(), typeMid, restoredCount, ms);
    }

    // ── STALL RECOVERY ────────────────────────────────────────────────────────
    public void LogStallRecovery(int reset, int failed)
    {
        if (reset > 0 || failed > 0)
            Log.Warning("{Time} | STALL RECOVERY   | Reset={Reset} | Failed={Failed}",
                Now(), reset, failed);
    }

    public void LogLogout(string typeMid, decimal deviceId)
    {
        Log.Information("{Time} | LOGOUT           | TypeMID={TypeMID} | Device={DeviceID}",
            Now(), typeMid, deviceId);
    }

   //typeMid, deviceId, eventData.ToString() ?? "No event data", reqTime, sw.ElapsedMilliseconds

    public void LogEvent(string typeMid, decimal deviceId, string eventData, DateTime reqTime, long ms)
    {
        Log.Information("{Time} | EVENT RECEIVED   | TypeMID={TypeMID} | Device={DeviceID} | EventData={EventData} | {Ms}ms",
            Now(), typeMid, deviceId, eventData, ms);
    }
}