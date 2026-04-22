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
    decimal deviceType, bool success, string detail, long durationMs)
{
    if (success)
    {
        _info.Information(
            "[LOGIN] TypeMID:{TypeMID} DeviceID:{DeviceID} Name:{Name} DeviceType:{DeviceType} Result:SUCCESS Duration:{Dur}ms",
            typeMid, deviceId, deviceName, deviceType, durationMs);

        _debug.Information(
            "[LOGIN] TypeMID:{TypeMID} DeviceID:{DeviceID} Name:{Name} DeviceType:{DeviceType} Result:SUCCESS Duration:{Dur}ms",
            typeMid, deviceId, deviceName, deviceType, durationMs);
    }
    else
    {
        _info.Warning(
            "[LOGIN] TypeMID:{TypeMID} DeviceID:{DeviceID} DeviceType:{DeviceType} Result:FAILED Reason:{Reason}",
            typeMid, deviceId, deviceType, detail);
        _debug.Warning(
            "[LOGIN] TypeMID:{TypeMID} DeviceID:{DeviceID} DeviceType:{DeviceType} Result:FAILED Reason:{Reason} Duration:{Dur}ms",
            typeMid, deviceId, deviceType, detail, durationMs);
        _error.Warning(
            "[LOGIN-FAIL] TypeMID:{TypeMID} DeviceID:{DeviceID} DeviceType:{DeviceType} Reason:{Reason}",
            typeMid, deviceId, deviceType, detail);
    }
}

// ── POLL DATA SENT ────────────────────────────────────────────────────────
public void LogPollDataSent(
    string typeMid, decimal deviceId, string deviceName, decimal deviceType,
    List<MatCommTrn> rows, int totalPending,
    DateTime reqTime, long durationMs)
{
    var ids      = string.Join(",", rows.Select(r => r.TrnID));
    var rowCount = rows.Count;

    _info.Information(
        "[POLL-SENT] TypeMID:{TypeMID} DeviceID:{DeviceID} DeviceType:{DeviceType} RowsSent:{Rows} TotalPending:{Pending} ReqTime:{ReqTime} Duration:{Dur}ms",
        typeMid, deviceId, deviceType, rowCount, totalPending,
        reqTime.ToString("HH:mm:ss.fff"), durationMs);

    _debug.Information(
        "[POLL-SENT] TypeMID:{TypeMID} DeviceID:{DeviceID} Name:{Name} DeviceType:{DeviceType} " +
        "RowsSent:{Rows} TotalPending:{Pending} TrnIDs:[{IDs}] ReqTime:{ReqTime} Duration:{Dur}ms",
        typeMid, deviceId, deviceName, deviceType,
        rowCount, totalPending, ids,
        reqTime.ToString("HH:mm:ss.fff"), durationMs);

    foreach (var row in rows)
        _debug.Information(
            "[POLL-ROW] TrnID:{TrnID} TypeMID:{TypeMID} DeviceType:{DeviceType} MsgStr:{MsgStr} RetryCnt:{Retry}",
            row.TrnID, typeMid, deviceType, row.MsgStr, row.RetryCnt);

    TestingLog("[POLL-SENT] TypeMID:{TypeMID} DeviceID:{DeviceID} DeviceType:{DeviceType} TrnIDs:[{IDs}] Pending:{Pending}",
        typeMid, deviceId, deviceType, ids, totalPending);
}

// ── POLL NO DATA ──────────────────────────────────────────────────────────
public void LogPollNoData(string typeMid, decimal deviceId, decimal deviceType,
    int totalPending, DateTime reqTime, long durationMs)
{
    _info.Information(
        "[POLL-EMPTY] TypeMID:{TypeMID} DeviceID:{DeviceID} DeviceType:{DeviceType} TotalPending:{Pending} ReqTime:{ReqTime} Duration:{Dur}ms",
        typeMid, deviceId, deviceType, totalPending,
        reqTime.ToString("HH:mm:ss.fff"), durationMs);

    _debug.Information(
        "[POLL-EMPTY] TypeMID:{TypeMID} DeviceID:{DeviceID} DeviceType:{DeviceType} TotalPending:{Pending} ReqTime:{ReqTime} Duration:{Dur}ms",
        typeMid, deviceId, deviceType, totalPending,
        reqTime.ToString("HH:mm:ss.fff"), durationMs);

    TestingLog("[POLL-EMPTY] TypeMID:{TypeMID} DeviceID:{DeviceID} DeviceType:{DeviceType} Pending:{Pending}",
        typeMid, deviceId, deviceType, totalPending);
}

// ── POLL NEED ACK FIRST ───────────────────────────────────────────────────
public void LogPollNeedAck(string typeMid, decimal deviceId, decimal deviceType,
    DateTime reqTime, long durationMs)
{
    _info.Warning(
        "[POLL-BLOCKED] TypeMID:{TypeMID} DeviceID:{DeviceID} DeviceType:{DeviceType} Reason:TrnStat1RowsExist ReqTime:{ReqTime} Duration:{Dur}ms",
        typeMid, deviceId, deviceType,
        reqTime.ToString("HH:mm:ss.fff"), durationMs);

    _debug.Warning(
        "[POLL-BLOCKED] TypeMID:{TypeMID} DeviceID:{DeviceID} DeviceType:{DeviceType} Reason:TrnStat1RowsExist ReqTime:{ReqTime} Duration:{Dur}ms",
        typeMid, deviceId, deviceType,
        reqTime.ToString("HH:mm:ss.fff"), durationMs);

    TestingLog("[POLL-BLOCKED] TypeMID:{TypeMID} DeviceID:{DeviceID} DeviceType:{DeviceType}",
        typeMid, deviceId, deviceType);
}

// ── ACK RECEIVED ──────────────────────────────────────────────────────────
public void LogAck(string typeMid, decimal deviceId, decimal deviceType,
    List<decimal> clientIds, AckResult result,
    DateTime t2, long serverMs,
    double upstreamMs, double downstreamMsPrev, double fullRoundTripPrev,
    int ackWarnSeconds)
{
    var ids      = string.Join(",", clientIds);
    var avgDelay = result.AckDelays.Count > 0
        ? Math.Round(result.AckDelays.Values.Average(), 2) : 0.0;
    var maxDelay = result.AckDelays.Count > 0
        ? result.AckDelays.Values.Max() : 0.0;

    var upLabel        = upstreamMs        >= 0 ? $"{upstreamMs}ms"        : "N/A";
    var downLabel      = downstreamMsPrev  >= 0 ? $"{downstreamMsPrev}ms"  : "N/A";
    var roundTripLabel = fullRoundTripPrev >= 0 ? $"{fullRoundTripPrev}ms" : "N/A";

    _info.Information(
        "[ACK] TypeMID:{TypeMID} DeviceID:{DeviceID} DeviceType:{DeviceType} Claimed:{Claimed} Updated:{Updated} " +
        "ServerMs:{Server}ms UpstreamMs:{Up} AvgDelay:{Avg}ms MaxDelay:{Max}ms T2:{T2}",
        typeMid, deviceId, deviceType,
        clientIds.Count, result.UpdatedCount,
        serverMs, upLabel, avgDelay, maxDelay,
        t2.ToString("HH:mm:ss.fff"));

    _debug.Information(
        "[ACK] TypeMID:{TypeMID} DeviceID:{DeviceID} DeviceType:{DeviceType} Claimed:{Claimed} Updated:{Updated} " +
        "TrnIDs:[{IDs}] ServerMs:{Server}ms UpstreamMs:{Up} DownstreamMs:{Down} FullRoundTrip:{Full} T2:{T2}",
        typeMid, deviceId, deviceType,
        clientIds.Count, result.UpdatedCount,
        ids, serverMs, upLabel, downLabel, roundTripLabel,
        t2.ToString("HH:mm:ss.fff"));

    foreach (var kv in result.AckDelays)
        _debug.Information(
            "[ACK-DELAY] TrnID:{TrnID} TypeMID:{TypeMID} DeviceType:{DeviceType} Delay:{Delay}ms",
            kv.Key, typeMid, deviceType, kv.Value);

    if (maxDelay > ackWarnSeconds)
        _error.Warning(
            "[ACK-SLOW] TypeMID:{TypeMID} DeviceID:{DeviceID} DeviceType:{DeviceType} MaxDelay:{Max}ms Threshold:{Threshold}s",
            typeMid, deviceId, deviceType, maxDelay, ackWarnSeconds);

    if (result.UpdatedCount == 0 && clientIds.Count > 0)
        _error.Error(
            "[ACK-ZERO-UPDATED] TypeMID:{TypeMID} DeviceID:{DeviceID} DeviceType:{DeviceType} ClaimedIDs:[{IDs}]",
            typeMid, deviceId, deviceType, ids);

    if (result.MismatchedIds.Count > 0)
        _error.Error(
            "[ACK-MISMATCH] TypeMID:{TypeMID} DeviceID:{DeviceID} DeviceType:{DeviceType} MissingTrnIDs:[{Missed}]",
            typeMid, deviceId, deviceType,
            string.Join(",", result.MismatchedIds));

    TestingLog(
        "[ACK] TypeMID:{TypeMID} DeviceID:{DeviceID} DeviceType:{DeviceType} Updated:{Updated} AvgDelay:{Avg}ms",
        typeMid, deviceId, deviceType, result.UpdatedCount, avgDelay);
}

// ── RESTORE ───────────────────────────────────────────────────────────────
public void LogRestore(string typeMid, decimal deviceId, decimal deviceType,
    int restoredCount, DateTime reqTime, long durationMs)
{
    _info.Warning(
        "[RESTORE] TypeMID:{TypeMID} DeviceID:{DeviceID} DeviceType:{DeviceType} RestoredRows:{Count} ReqTime:{ReqTime} Duration:{Dur}ms",
        typeMid, deviceId, deviceType, restoredCount,
        reqTime.ToString("HH:mm:ss.fff"), durationMs);

    _debug.Warning(
        "[RESTORE] TypeMID:{TypeMID} DeviceID:{DeviceID} DeviceType:{DeviceType} RestoredRows:{Count} ReqTime:{ReqTime} Duration:{Dur}ms",
        typeMid, deviceId, deviceType, restoredCount,
        reqTime.ToString("HH:mm:ss.fff"), durationMs);
}

// ── REFRESH ───────────────────────────────────────────────────────────────
public void LogRefresh(string typeMid, decimal deviceId, decimal deviceType,
    bool success, long durationMs)
{
    if (success)
    {
        _info.Information(
            "[REFRESH] TypeMID:{TypeMID} DeviceID:{DeviceID} DeviceType:{DeviceType} Result:SUCCESS Duration:{Dur}ms",
            typeMid, deviceId, deviceType, durationMs);
        _debug.Information(
            "[REFRESH] TypeMID:{TypeMID} DeviceID:{DeviceID} DeviceType:{DeviceType} Result:SUCCESS Duration:{Dur}ms",
            typeMid, deviceId, deviceType, durationMs);
    }
    else
    {
        _info.Warning(
            "[REFRESH] TypeMID:{TypeMID} DeviceID:{DeviceID} DeviceType:{DeviceType} Result:FAILED Duration:{Dur}ms",
            typeMid, deviceId, deviceType, durationMs);
        _error.Warning(
            "[REFRESH-FAIL] TypeMID:{TypeMID} DeviceID:{DeviceID} DeviceType:{DeviceType} Duration:{Dur}ms",
            typeMid, deviceId, deviceType, durationMs);
    }
}

// ── TIMING (EVENT) ────────────────────────────────────────────────────────
public void LogTiming(string tag, string typeMid, decimal deviceId,
    decimal deviceType, DateTime? t1, DateTime t2, DateTime t3)
{
    double upstreamMs = -1;
    double fullMs     = -1;
    long   serverMs   = (long)(t3 - t2).TotalMilliseconds;

    if (t1.HasValue)
    {
        upstreamMs = Math.Round((t2 - t1.Value).TotalMilliseconds, 1);
        fullMs     = Math.Round((t3 - t1.Value).TotalMilliseconds, 1);
    }

    _info.Information(
        "[{Tag}-TIMING] TypeMID:{TypeMID} DeviceID:{DeviceID} DeviceType:{DeviceType} " +
        "UpstreamMs:{Up} ServerMs:{Server}ms FullMs:{Full} T1:{T1} T2:{T2} T3:{T3}",
        tag, typeMid, deviceId, deviceType,
        upstreamMs >= 0 ? $"{upstreamMs}ms" : "N/A",
        serverMs,
        fullMs >= 0 ? $"{fullMs}ms" : "N/A",
        t1?.ToString("HH:mm:ss.fff") ?? "N/A",
        t2.ToString("HH:mm:ss.fff"),
        t3.ToString("HH:mm:ss.fff"));

    _debug.Information(
        "[{Tag}-TIMING] TypeMID:{TypeMID} DeviceID:{DeviceID} DeviceType:{DeviceType} " +
        "UpstreamMs:{Up} ServerMs:{Server}ms FullMs:{Full} T1:{T1} T2:{T2} T3:{T3}",
        tag, typeMid, deviceId, deviceType,
        upstreamMs >= 0 ? $"{upstreamMs}ms" : "N/A",
        serverMs,
        fullMs >= 0 ? $"{fullMs}ms" : "N/A",
        t1?.ToString("HH:mm:ss.fff") ?? "N/A",
        t2.ToString("HH:mm:ss.fff"),
        t3.ToString("HH:mm:ss.fff"));
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

    public void LogAckTiming(string typeMid, decimal deviceId,
    long serverMs, double roundTripMs, double clientMs)
    {
        _info.Information(
            "[ACK-TIMING] TypeMID:{TypeMID} DeviceID:{DeviceID} " +
            "ServerMs:{Server}ms RoundTripMs:{RoundTrip}ms ClientMs:{Client}ms",
            typeMid, deviceId, serverMs, 
            Math.Round(roundTripMs, 1), 
            Math.Round(clientMs, 1));

        _debug.Information(
            "[ACK-TIMING] TypeMID:{TypeMID} DeviceID:{DeviceID} " +
            "ServerMs:{Server}ms RoundTripMs:{RoundTrip}ms ClientMs:{Client}ms",
            typeMid, deviceId, serverMs,
            Math.Round(roundTripMs, 1),
            Math.Round(clientMs, 1));
    }

    // ── GENERAL EVENT LOGGING ─────────────────────────────────────────────────
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

    // ── TIMING CORE (used by ACK + EVENT) ─────────────────────────────────────

    public void LogTiming(
        string tag,
        string typeMid,
        decimal deviceId,
        DateTime? t1,   // client send time
        DateTime t2,    // server receive
        DateTime t3)    // server respond
    {
        double upstreamMs = -1;
        double fullMs     = -1;
        long   serverMs   = (long)(t3 - t2).TotalMilliseconds;

        if (t1.HasValue)
        {
            upstreamMs = Math.Round((t2 - t1.Value).TotalMilliseconds, 1);
            fullMs     = Math.Round((t3 - t1.Value).TotalMilliseconds, 1);
        }

        _info.Information(
            "[{Tag}-TIMING] TypeMID:{TypeMID} DeviceID:{DeviceID} " +
            "UpstreamMs:{Up} ServerMs:{Server}ms FullRoundTripMs:{Full} " +
            "T1:{T1} T2:{T2} T3:{T3}",
            tag, typeMid, deviceId,
            upstreamMs >= 0 ? $"{upstreamMs}ms" : "N/A",
            serverMs,
            fullMs >= 0 ? $"{fullMs}ms" : "N/A",
            t1?.ToString("HH:mm:ss.fff") ?? "N/A",
            t2.ToString("HH:mm:ss.fff"),
            t3.ToString("HH:mm:ss.fff"));

        _debug.Information(
            "[{Tag}-TIMING] TypeMID:{TypeMID} DeviceID:{DeviceID} " +
            "UpstreamMs:{Up} ServerMs:{Server}ms FullRoundTripMs:{Full} " +
            "T1:{T1} T2:{T2} T3:{T3}",
            tag, typeMid, deviceId,
            upstreamMs >= 0 ? $"{upstreamMs}ms" : "N/A",
            serverMs,
            fullMs >= 0 ? $"{fullMs}ms" : "N/A",
            t1?.ToString("HH:mm:ss.fff") ?? "N/A",
            t2.ToString("HH:mm:ss.fff"),
            t3.ToString("HH:mm:ss.fff"));

        TestingLog(
            "[{Tag}-TIMING] Up:{Up} Server:{Server} Full:{Full}",
            tag,
            upstreamMs,
            serverMs,
            fullMs);
    }
 // ── BULK EVENT ────────────────────────────────────────────────────────────

// ── BULK EVENT ────────────────────────────────────────────────────────────
public void LogBulkEvent(
    string typeMid, decimal deviceId, decimal? deviceType,
    int count, DateTime reqTime, long serverMs,
    DateTime? t1, DateTime t2, DateTime t3)
{
    double upstreamMs = t1.HasValue
        ? Math.Round((t2 - t1.Value).TotalMilliseconds, 1) : -1;
    double fullMs = t1.HasValue
        ? Math.Round((t3 - t1.Value).TotalMilliseconds, 1) : -1;

    var upLabel   = upstreamMs >= 0 ? $"{upstreamMs}ms" : "N/A";
    var fullLabel = fullMs     >= 0 ? $"{fullMs}ms"     : "N/A";

    _info.Information(
        "[EVENT-SENT] TypeMID:{TypeMID} DeviceID:{DeviceID} DeviceType:{DeviceType} " +
        "Count:{Count} ServerMs:{Server}ms UpstreamMs:{Up} FullMs:{Full} " +
        "ReqTime:{ReqTime} T1:{T1} T2:{T2} T3:{T3}",
        typeMid, deviceId, deviceType,
        count, serverMs, upLabel, fullLabel,
        reqTime.ToString("HH:mm:ss.fff"),
        t1?.ToString("HH:mm:ss.fff") ?? "N/A",
        t2.ToString("HH:mm:ss.fff"),
        t3.ToString("HH:mm:ss.fff"));

    _debug.Information(
        "[EVENT-SENT] TypeMID:{TypeMID} DeviceID:{DeviceID} DeviceType:{DeviceType} " +
        "Count:{Count} ServerMs:{Server}ms UpstreamMs:{Up} FullMs:{Full} " +
        "ReqTime:{ReqTime} T1:{T1} T2:{T2} T3:{T3}",
        typeMid, deviceId, deviceType,
        count, serverMs, upLabel, fullLabel,
        reqTime.ToString("HH:mm:ss.fff"),
        t1?.ToString("HH:mm:ss.fff") ?? "N/A",
        t2.ToString("HH:mm:ss.fff"),
        t3.ToString("HH:mm:ss.fff"));

    TestingLog(
        "[EVENT-SENT] TypeMID:{TypeMID} DeviceID:{DeviceID} Count:{Count} Server:{Server}ms Up:{Up} Full:{Full}",
        typeMid, deviceId, count, serverMs, upLabel, fullLabel);
}
}
