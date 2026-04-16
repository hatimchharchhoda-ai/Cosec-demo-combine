using Serilog;

namespace MatPoll.Services;

public class ActivityLogger
{
    private static string Now() =>
        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"); // ✅ your format

    // ── LOGIN ─────────────────────────────────────────────
    public void LogLogin(string typeMid, decimal deviceId, string deviceName,
        bool success, string failReason, long ms)
    {
        if (success)
        {
            Log.Information("{Time} | LOGIN SUCCESS | TypeMID={TypeMID} | Device={DeviceName} ({DeviceID}) | {Ms}ms",
                Now(), typeMid, deviceName, deviceId, ms);
        }
        else
        {
            Log.Warning("{Time} | LOGIN FAILED | TypeMID={TypeMID} | Device={DeviceID} | Reason={Reason}",
                Now(), typeMid, deviceId, failReason);
        }
    }

    // ── TOKEN REFRESH ─────────────────────────────────────
    public void LogRefresh(string typeMid, decimal deviceId, bool success, long ms)
    {
        if (success)
        {
            Log.Information("{Time} | TOKEN REFRESHED | TypeMID={TypeMID} | Device={DeviceID} | {Ms}ms",
                Now(), typeMid, deviceId, ms);
        }
        else
        {
            Log.Warning("{Time} | REFRESH FAILED | TypeMID={TypeMID} | Device={DeviceID}",
                Now(), typeMid, deviceId);
        }
    }

    // ── DATA SENT (INFO - SUMMARY ONLY) ───────────────────
    public void LogDataSent(string typeMid, int rows, int pending)
    {
        Log.Information("{Time} | DATA SENT | TypeMID={TypeMID} | Rows={Rows} | Pending={Pending}",
            Now(), typeMid, rows, pending);
    }

    // ── DATA SENT (DEBUG - FULL DETAILS) ──────────────────
    public void LogDataSentDebug(string typeMid, decimal deviceId, string deviceName,
        List<decimal> trnIds, List<int?> retryCnt,
        string msgStr, int totalRows, int pending)
    {
        Log.Debug(
@"{Time} | DATA SENT DETAILS
TypeMID={TypeMID}
DeviceID={DeviceID}
DeviceName={DeviceName}
TrnIDs={TrnIDs}
RetryCnt={RetryCnt}
MsgStr={MsgStr}
TotalRows={TotalRows}
Pending={Pending}",
            Now(),
            typeMid,
            deviceId,
            deviceName,
            string.Join(",", trnIds),
            string.Join(",", retryCnt),
            msgStr,
            totalRows,
            pending);
    }

    // ── NO DATA ───────────────────────────────────────────
    public void LogNoData(string typeMid, int pending, long ms)
    {
        Log.Information("{Time} | NO DATA | TypeMID={TypeMID} | Pending={Pending} | {Ms}ms",
            Now(), typeMid, pending, ms);
    }

    // ── NEED ACK FIRST ────────────────────────────────────
    public void LogNeedAck(string typeMid, int pending)
    {
        Log.Information("{Time} | NEED ACK FIRST | TypeMID={TypeMID} | Pending={Pending}",
            Now(), typeMid, pending);
    }

    // ── ACK RECEIVED (INFO) ───────────────────────────────
    public void LogAck(string typeMid, int rows, double delaySec)
    {
        Log.Information("{Time} | ACK RECEIVED | TypeMID={TypeMID} | Rows={Rows} | Delay={Delay}s",
            Now(), typeMid, rows, delaySec);
    }

    // ── ACK MISMATCH (ERROR) ──────────────────────────────
    public void LogAckMismatch(string typeMid,
        List<decimal> clientIds,
        List<decimal> dbUpdated)
    {
        var missing = clientIds.Except(dbUpdated).ToList();

        if (missing.Any())
        {
            Log.Error(
@"{Time} | ACK MISMATCH
TypeMID={TypeMID}
ClientSent={Client}
UpdatedInDB={DB}
Missing={Missing}",
                Now(),
                typeMid,
                string.Join(",", clientIds),
                string.Join(",", dbUpdated),
                string.Join(",", missing));
        }
    }

    // ── DEVICE NOT ACKING ─────────────────────────────────
    public void LogNoAck(string typeMid, int pending, DateTime lastSent)
    {
        Log.Warning("{Time} | DEVICE NOT ACKING | TypeMID={TypeMID} | Pending={Pending} | LastSent={Last}",
            Now(), typeMid, pending, lastSent.ToString("yyyy-MM-dd HH:mm:ss"));
    }

    // ── RESTORE ───────────────────────────────────────────
    public void LogRestore(string typeMid, int rows, long ms)
    {
        Log.Information("{Time} | RESTORE | TypeMID={TypeMID} | Rows={Rows} | {Ms}ms",
            Now(), typeMid, rows, ms);
    }

    // ── STALL RECOVERY ────────────────────────────────────
    public void LogStallRecovery(int reset, int failed)
    {
        if (reset > 0 || failed > 0)
        {
            Log.Warning("{Time} | STALL RECOVERY | Reset={Reset} | Failed={Failed}",
                Now(), reset, failed);
        }
    }

    // ── LOGOUT ────────────────────────────────────────────
    public void LogLogout(string typeMid, decimal deviceId)
    {
        Log.Information("{Time} | LOGOUT | TypeMID={TypeMID} | Device={DeviceID}",
            Now(), typeMid, deviceId);
    }

    // ── EVENT RECEIVED ────────────────────────────────────
    public void LogEvent(string typeMid, decimal deviceId, string eventData, long ms)
    {
        Log.Information("{Time} | EVENT RECEIVED | TypeMID={TypeMID} | Device={DeviceID} | EventData={EventData} | {Ms}ms",
            Now(), typeMid, deviceId, eventData, ms);
    }

    // ── GLOBAL EXCEPTION ──────────────────────────────────
    public void LogException(string typeMid, string source, Exception ex)
    {
        Log.Error(ex,
            "{Time} | ERROR | TypeMID={TypeMID} | Source={Source} | Message={Message}",
            Now(), typeMid, source, ex.Message);
    }
}