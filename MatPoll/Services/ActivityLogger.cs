using MatPoll.Models;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace MatPoll.Services;

// ─────────────────────────────────────────────────────────────────────────────
// ActivityLogger  —  structured logging to 4 separate log files
//
// How the 4 sinks work:
//   info.log    → only events tagged with ForContext("Sink","info")
//   debug.log   → events tagged "debug" (includes everything info has + more detail)
//   error.log   → events tagged "error" (exceptions, mismatches, stalls, DB failures)
//   testing.log → events tagged "testing" (only active when TestingLog=true in config)
//
// Each public method writes to the correct sinks based on severity.
// Controllers and services call this instead of writing to ILogger directly.
// ─────────────────────────────────────────────────────────────────────────────

public class ActivityLogger
{
    private readonly bool _testingEnabled;

    // Named loggers — each routes to a different file sink
    private static readonly Serilog.ILogger _info    = Log.ForContext("Sink", "info");
    private static readonly Serilog.ILogger _debug   = Log.ForContext("Sink", "debug");
    private static readonly Serilog.ILogger _error   = Log.ForContext("Sink", "error");
    private static Serilog.ILogger          _testing = Log.ForContext("Sink", "testing");

    public ActivityLogger(IConfiguration config)
    {
        _testingEnabled = config.GetValue<bool>("TestingLog", false);
    }

    // ── LOGIN ─────────────────────────────────────────────────────────────────

    public void LogLogin(string typeMid, decimal deviceId, string deviceName,
        bool success, string detail, long durationMs)
    {
        if (success)
        {
            // info: one clean line
            _info.Information(
                "[LOGIN] TypeMID:{TypeMID} DeviceID:{DeviceID} Name:{Name} Result:SUCCESS Duration:{Dur}ms",
                typeMid, deviceId, deviceName, durationMs);

            // debug: same line (info events always appear in debug too)
            _debug.Information(
                "[LOGIN] TypeMID:{TypeMID} DeviceID:{DeviceID} Name:{Name} Result:SUCCESS Duration:{Dur}ms",
                typeMid, deviceId, deviceName, durationMs);

            TestingLog("[LOGIN] SUCCESS TypeMID:{TypeMID} DeviceID:{DeviceID} Name:{Name} Duration:{Dur}ms",
                typeMid, deviceId, deviceName, durationMs);

            TestingLog("[LOGIN-DETAIL] TypeMID:{TypeMID} DeviceID:{DeviceID} Detail:{Detail}",
                typeMid, deviceId, detail);
        }
        else
        {
            // Failed login → error log
            _info.Warning(
                "[LOGIN] TypeMID:{TypeMID} DeviceID:{DeviceID} Result:FAILED Reason:{Reason}",
                typeMid, deviceId, detail);
            _debug.Warning(
                "[LOGIN] TypeMID:{TypeMID} DeviceID:{DeviceID} Result:FAILED Reason:{Reason} Duration:{Dur}ms",
                typeMid, deviceId, detail, durationMs);
            _error.Warning(
                "[LOGIN-FAIL] TypeMID:{TypeMID} DeviceID:{DeviceID} Reason:{Reason}",
                typeMid, deviceId, detail);

            TestingLog("[LOGIN] FAILED TypeMID:{TypeMID} DeviceID:{DeviceID} Reason:{Reason}",
                typeMid, deviceId, detail);
        }
    }

    // ── POLL DATA SENT ────────────────────────────────────────────────────────

    public void LogPollDataSent(
        string typeMid, decimal deviceId, string deviceName,
        List<MatCommTrn> rows, int totalPending,
        DateTime reqTime, long durationMs)
    {
        var ids      = string.Join(",", rows.Select(r => r.TrnID));
        var rowCount = rows.Count;

        // info: summary line only
        _info.Information(
            "[POLL-SENT] TypeMID:{TypeMID} DeviceID:{DeviceID} RowsSent:{Rows} TotalPending:{Pending} ReqTime:{ReqTime} Duration:{Dur}ms",
            typeMid, deviceId, rowCount, totalPending,
            reqTime.ToString("HH:mm:ss.fff"), durationMs);

        // debug: full detail — IDs, messages, retry counts
        _debug.Information(
            "[POLL-SENT] TypeMID:{TypeMID} DeviceID:{DeviceID} Name:{Name} " +
            "RowsSent:{Rows} TotalPending:{Pending} " +
            "TrnIDs:[{IDs}] ReqTime:{ReqTime} Duration:{Dur}ms",
            typeMid, deviceId, deviceName,
            rowCount, totalPending,
            ids, reqTime.ToString("HH:mm:ss.fff"), durationMs);

        // debug: one line per row with full MsgStr
        foreach (var row in rows)
        {
            _debug.Information(
                "[POLL-ROW] TrnID:{TrnID} TypeMID:{TypeMID} MsgStr:{MsgStr} RetryCnt:{Retry} ",
                row.TrnID, typeMid, row.MsgStr, row.RetryCnt
               );
        }

        TestingLog("[POLL-SENT] TypeMID:{TypeMID} DeviceID:{DeviceID} TrnIDs:[{IDs}] Pending:{Pending}",
            typeMid, deviceId, ids, totalPending);
    }

    // ── POLL NO DATA ──────────────────────────────────────────────────────────

    public void LogPollNoData(string typeMid, decimal deviceId,
        int totalPending, DateTime reqTime, long durationMs)
    {
        _info.Information(
            "[POLL-EMPTY] TypeMID:{TypeMID} DeviceID:{DeviceID} TotalPending:{Pending} ReqTime:{ReqTime} Duration:{Dur}ms",
            typeMid, deviceId, totalPending,
            reqTime.ToString("HH:mm:ss.fff"), durationMs);

        _debug.Information(
            "[POLL-EMPTY] TypeMID:{TypeMID} DeviceID:{DeviceID} TotalPending:{Pending} ReqTime:{ReqTime} Duration:{Dur}ms",
            typeMid, deviceId, totalPending,
            reqTime.ToString("HH:mm:ss.fff"), durationMs);

        TestingLog("[POLL-EMPTY] TypeMID:{TypeMID} DeviceID:{DeviceID} Pending:{Pending}",
            typeMid, deviceId, totalPending);
    }

    // ── POLL NEED ACK FIRST ───────────────────────────────────────────────────

    public void LogPollNeedAck(string typeMid, decimal deviceId,
        DateTime reqTime, long durationMs)
    {
        _info.Warning(
            "[POLL-BLOCKED] TypeMID:{TypeMID} DeviceID:{DeviceID} Reason:TrnStat1RowsExist ReqTime:{ReqTime} Duration:{Dur}ms",
            typeMid, deviceId,
            reqTime.ToString("HH:mm:ss.fff"), durationMs);

        _debug.Warning(
            "[POLL-BLOCKED] TypeMID:{TypeMID} DeviceID:{DeviceID} Reason:TrnStat1RowsExist ReqTime:{ReqTime} Duration:{Dur}ms",
            typeMid, deviceId,
            reqTime.ToString("HH:mm:ss.fff"), durationMs);

        TestingLog("[POLL-BLOCKED] TypeMID:{TypeMID} DeviceID:{DeviceID} — device has un-ACKed rows",
            typeMid, deviceId);
    }

    // ── ACK RECEIVED ──────────────────────────────────────────────────────────

    public void LogAck(string typeMid, decimal deviceId,
        List<decimal> clientIds, AckResult result,
        DateTime reqTime, long durationMs,
        int ackWarnSeconds)
    {
        var ids     = string.Join(",", clientIds);
        var avgDelay = result.AckDelays.Count > 0
            ? Math.Round(result.AckDelays.Values.Average(), 2)
            : 0.0;
        var maxDelay = result.AckDelays.Count > 0
            ? result.AckDelays.Values.Max()
            : 0.0;

        // info: summary
        _info.Information(
            "[ACK] TypeMID:{TypeMID} DeviceID:{DeviceID} Claimed:{Claimed} Updated:{Updated} AvgDelay:{Avg}s MaxDelay:{Max}s ReqTime:{ReqTime} Duration:{Dur}ms",
            typeMid, deviceId,
            clientIds.Count, result.UpdatedCount,
            avgDelay, maxDelay,
            reqTime.ToString("HH:mm:ss.fff"), durationMs);

        // debug: full detail
        _debug.Information(
            "[ACK] TypeMID:{TypeMID} DeviceID:{DeviceID} Claimed:{Claimed} Updated:{Updated} " +
            "TrnIDs:[{IDs}] AvgDelay:{Avg}s MaxDelay:{Max}s " +
            "ReqTime:{ReqTime} Duration:{Dur}ms",
            typeMid, deviceId,
            clientIds.Count, result.UpdatedCount,
            ids, avgDelay, maxDelay,
            reqTime.ToString("HH:mm:ss.fff"), durationMs);

        // debug: per-row delay
        foreach (var kv in result.AckDelays)
        {
            _debug.Information(
                "[ACK-DELAY] TrnID:{TrnID} TypeMID:{TypeMID} Delay:{Delay}s",
                kv.Key, typeMid, kv.Value);
        }

        TestingLog("[ACK] TypeMID:{TypeMID} DeviceID:{DeviceID} Claimed:{Claimed} Updated:{Updated} AvgDelay:{Avg}s MaxDelay:{Max}s",
            typeMid, deviceId,
            clientIds.Count, result.UpdatedCount,
            avgDelay, maxDelay);

        // error: slow ACK warning
        if (maxDelay > ackWarnSeconds)
        {
            _error.Warning(
                "[ACK-SLOW] TypeMID:{TypeMID} DeviceID:{DeviceID} MaxDelay:{Max}s " +
                "Threshold:{Threshold}s TrnIDs:[{IDs}]",
                typeMid, deviceId, maxDelay, ackWarnSeconds, ids);
        }

        // error: mismatch — client said these IDs but DB didn't find them
        if (result.MismatchedIds.Count > 0)
        {
            var missed = string.Join(",", result.MismatchedIds);
            _error.Error(
                "[ACK-MISMATCH] TypeMID:{TypeMID} DeviceID:{DeviceID} " +
                "ClientClaimed:{Claimed} DBUpdated:{Updated} " +
                "MissingTrnIDs:[{Missed}]",
                typeMid, deviceId,
                clientIds.Count, result.UpdatedCount, missed);
        }

        TestingLog("[ACK] TypeMID:{TypeMID} DeviceID:{DeviceID} TrnIDs:[{IDs}] Updated:{Updated} AvgDelay:{Avg}s",
            typeMid, deviceId, ids, result.UpdatedCount, avgDelay);
    }

    // ── RESTORE ───────────────────────────────────────────────────────────────

    public void LogRestore(string typeMid, decimal deviceId,
        int restoredCount, DateTime reqTime, long durationMs)
    {
        _info.Warning(
            "[RESTORE] TypeMID:{TypeMID} DeviceID:{DeviceID} RestoredRows:{Count} ReqTime:{ReqTime} Duration:{Dur}ms",
            typeMid, deviceId, restoredCount,
            reqTime.ToString("HH:mm:ss.fff"), durationMs);

        _debug.Warning(
            "[RESTORE] TypeMID:{TypeMID} DeviceID:{DeviceID} RestoredRows:{Count} ReqTime:{ReqTime} Duration:{Dur}ms",
            typeMid, deviceId, restoredCount,
            reqTime.ToString("HH:mm:ss.fff"), durationMs);

        TestingLog("[RESTORE] TypeMID:{TypeMID} DeviceID:{DeviceID} RestoredRows:{Count}",
            typeMid, deviceId, restoredCount);
    }

    // ── TOKEN REFRESH ─────────────────────────────────────────────────────────

    public void LogRefresh(string typeMid, decimal deviceId,
        bool success, long durationMs)
    {
        if (success)
        {
            _info.Information(
                "[REFRESH] TypeMID:{TypeMID} DeviceID:{DeviceID} Result:SUCCESS Duration:{Dur}ms",
                typeMid, deviceId, durationMs);
            _debug.Information(
                "[REFRESH] TypeMID:{TypeMID} DeviceID:{DeviceID} Result:SUCCESS Duration:{Dur}ms",
                typeMid, deviceId, durationMs);
        }
        else
        {
            _info.Warning(
                "[REFRESH] TypeMID:{TypeMID} DeviceID:{DeviceID} Result:FAILED Duration:{Dur}ms",
                typeMid, deviceId, durationMs);
            _error.Warning(
                "[REFRESH-FAIL] TypeMID:{TypeMID} DeviceID:{DeviceID} Duration:{Dur}ms",
                typeMid, deviceId, durationMs);
        }

        TestingLog("[REFRESH] TypeMID:{TypeMID} DeviceID:{DeviceID} Success:{Success}",
            typeMid, deviceId, success);
    }

    // ── STALL RECOVERY ────────────────────────────────────────────────────────

    public void LogStallRecovery(List<StalledGroup> groups)
    {
        if (groups.Count == 0)
        {
            _debug.Information("[STALL-CHECK] No stalled rows found");
            TestingLog("[STALL-CHECK] Clean — no stalled rows");
            return;
        }

        foreach (var g in groups)
        {
            // info: one warning line per affected device
            _info.Warning(
                "[STALL] TypeMID:{TypeMID} StalledRows:{Total} Reset:{Reset} Failed:{Failed} MaxRetry:{MaxRetry}",
                g.TypeMID, g.RowCount, g.ResetCount, g.FailedCount, g.MaxRetry);

            // error: stall means device did not ACK — this is a problem
            _error.Warning(
                "[STALL-DEVICE] TypeMID:{TypeMID} StalledRows:{Total} Reset:{Reset} PermanentlyFailed:{Failed} MaxRetry:{MaxRetry} — device did not ACK in time",
                g.TypeMID, g.RowCount, g.ResetCount, g.FailedCount, g.MaxRetry);

            _debug.Warning(
                "[STALL] TypeMID:{TypeMID} StalledRows:{Total} Reset:{Reset} Failed:{Failed} MaxRetry:{MaxRetry}",
                g.TypeMID, g.RowCount, g.ResetCount, g.FailedCount, g.MaxRetry);

            TestingLog("[STALL] TypeMID:{TypeMID} Rows:{Total} Reset:{Reset} Failed:{Failed}",
                g.TypeMID, g.RowCount, g.ResetCount, g.FailedCount);
        }
    }

    // ── EXCEPTION / DB ERROR ──────────────────────────────────────────────────

    public void LogException(string action, string typeMid,
        decimal deviceId, Exception ex)
    {
        // Goes ONLY to error.log — never clutters info or debug
        _error.Error(ex,
            "[EXCEPTION] Action:{Action} TypeMID:{TypeMID} DeviceID:{DeviceID} Error:{Msg}",
            action, typeMid, deviceId, ex.Message);

        TestingLog("[EXCEPTION] Action:{Action} TypeMID:{TypeMID} Error:{Msg}",
            action, typeMid, ex.Message);
    }

    public void LogDbFailure(string action, string typeMid, Exception ex)
    {
        _error.Error(ex,
            "[DB-ERROR] Action:{Action} TypeMID:{TypeMID} Error:{Msg}",
            action, typeMid, ex.Message);

        TestingLog("[DB-ERROR] Action:{Action} TypeMID:{TypeMID} Error:{Msg}",
            action, typeMid, ex.Message);
    }

    // ── TESTING INTERNAL STEPS ────────────────────────────────────────────────

    public void LogTestingStep(string step, params object?[] args)
    {
        TestingLog(step, args);
    }

    // ── Private helper ────────────────────────────────────────────────────────

    private void TestingLog(string template, params object?[] args)
    {
        if (!_testingEnabled) return;
        _testing.Debug(template, args);
    }

    // ── EVENT LOGGING ──────────────────────────────────────────────────────────
    public void LogEvent(
    string typeMid,
    decimal deviceId,
    string message,
    DateTime requestTime,
    long durationMs)
    {
        _info.Information(
            "[EVENT] TypeMID:{TypeMID} DeviceID:{DeviceID} Message:{Message} ReqTime:{ReqTime} Duration:{Dur}ms",
            typeMid, deviceId, message, requestTime, durationMs);

        _debug.Information(
            "[EVENT] TypeMID:{TypeMID} DeviceID:{DeviceID} Message:{Message} ReqTime:{ReqTime} Duration:{Dur}ms",
            typeMid, deviceId, message, requestTime, durationMs);

        TestingLog(
            "[EVENT] TypeMID:{TypeMID} DeviceID:{DeviceID} Message:{Message}",
            typeMid, deviceId, message);
    }
}
