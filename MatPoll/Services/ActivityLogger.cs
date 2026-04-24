using MatPoll.Models;
using Serilog;

namespace MatPoll.Services;

// ─────────────────────────────────────────────────────────────────────────────
// ActivityLogger — human-readable logs, no TypeMID shown
//
// Log files:
//   info.log    → clean summary: one REQ+RES block per operation
//   debug.log   → same + timing details (T1/T2/T3)
//   error.log   → failures, mismatches, stalls, exceptions only
//   testing.log → internal steps (only when TestingLog=true)
// ─────────────────────────────────────────────────────────────────────────────

public class ActivityLogger
{
    private readonly bool _testingEnabled;

    private static readonly Serilog.ILogger _info    = Log.ForContext("Sink", "info");
    private static readonly Serilog.ILogger _debug   = Log.ForContext("Sink", "debug");
    private static readonly Serilog.ILogger _error   = Log.ForContext("Sink", "error");
    private static Serilog.ILogger          _testing = Log.ForContext("Sink", "testing");

    public ActivityLogger(IConfiguration config)
    {
        _testingEnabled = config.GetValue<bool>("TestingLog", false);
    }

    // ── LOGIN ─────────────────────────────────────────────────────────────────
    public void LogLogin(
        string typeMid, decimal deviceId, string deviceName,
        decimal deviceType, bool success, string detail, long durationMs,
        string mac = "", string ip = "")
    {
        if (success)
        {
            _info.Information(
                "[LOGIN]\n" +
                "   [REQ] Device:{Name}  Type:{DeviceType}  MAC:{MAC}  IP:{IP}\n" +
                "   [RES] SUCCESS  DeviceID:{DeviceID}  Duration:{Duration}ms",
                deviceName, deviceType, mac, ip,
                deviceId, durationMs);

            _debug.Information(
                "[LOGIN]\n" +
                "   [REQ] Device:{Name}  Type:{DeviceType}  MAC:{MAC}  IP:{IP}\n" +
                "   [RES] SUCCESS  DeviceID:{DeviceID}  Duration:{Duration}ms",
                deviceName, deviceType, mac, ip,
                deviceId, durationMs);
        }
        else
        {
            _info.Warning(
                "[LOGIN]\n" +
                "   [REQ] Device:{Name}  Type:{DeviceType}  MAC:{MAC}  IP:{IP}\n" +
                "   [RES] FAILED  Reason:{Reason}  Duration:{Duration}ms",
                deviceName, deviceType, mac, ip,
                detail, durationMs);

            _debug.Warning(
                "[LOGIN]\n" +
                "   [REQ] Device:{Name}  Type:{DeviceType}  MAC:{MAC}  IP:{IP}\n" +
                "   [RES] FAILED  Reason:{Reason}  Duration:{Duration}ms",
                deviceName, deviceType, mac, ip,
                detail, durationMs);

            _error.Warning(
                "[LOGIN-FAIL] Device:{Name}  Type:{DeviceType}  Reason:{Reason}",
                deviceName, deviceType, detail);
        }
    }

    // ── POLL DATA SENT ────────────────────────────────────────────────────────
    public void LogPollDataSent(
        string typeMid, decimal deviceId, string deviceName, decimal deviceType,
        List<MatCommTrn> rows, long totalPending,
        DateTime reqTime, long durationMs)
    {
        var rowCount   = rows.Count;
        var firstId    = rows.First().TrnID;
        var lastId     = rows.Last().TrnID;
        var idRange    = rowCount == 1 ? $"{firstId}" : $"{firstId}-{lastId}";

        // show first and last message so reader knows what was sent
        var firstMsg   = rows.First().MsgStr ?? "";
        var lastMsg    = rows.Last().MsgStr  ?? "";
        var msgSummary = rowCount == 1
            ? $"{firstMsg}"
            : $"{firstMsg} .. {lastMsg}";

        _info.Information(
            "[POLL]\n" +
            "   [REQ] Device:{Name}  DeviceID:{DeviceID}  Type:{DeviceType}\n" +
            "   [RES] Sent:{Sent}  Messages:[{Messages}]  IDs:[{IDs}]  Pending:{Pending}  Duration:{Duration}ms",
            deviceName, deviceId, deviceType,
            rowCount, msgSummary, idRange, totalPending, durationMs);

        _debug.Information(
            "[POLL]\n" +
            "   [REQ] Device:{Name}  DeviceID:{DeviceID}  Type:{DeviceType}  ReqTime:{ReqTime}\n" +
            "   [RES] Sent:{Sent}  Messages:[{Messages}]  IDs:[{IDs}]  Pending:{Pending}  Duration:{Duration}ms",
            deviceName, deviceId, deviceType,
            reqTime.ToString("HH:mm:ss.fff"),
            rowCount, msgSummary, idRange, totalPending, durationMs);

        TestingLog("[POLL] Device:{Name}  DeviceID:{DeviceID}  Sent:{Sent}  IDs:[{IDs}]",
            deviceName, deviceId, rowCount, idRange);
    }

    // ── POLL NO DATA ──────────────────────────────────────────────────────────
    public void LogPollNoData(
        string typeMid, decimal deviceId, decimal deviceType,
        DateTime reqTime, long durationMs)
    {
        _info.Information(
            "[POLL]\n" +
            "   [REQ] DeviceID:{DeviceID}  Type:{DeviceType}\n" +
            "   [RES] No pending messages  Duration:{Duration}ms",
            deviceId, deviceType, durationMs);

        _debug.Information(
            "[POLL]\n" +
            "   [REQ] DeviceID:{DeviceID}  Type:{DeviceType}  ReqTime:{ReqTime}\n" +
            "   [RES] No pending messages  Duration:{Duration}ms",
            deviceId, deviceType,
            reqTime.ToString("HH:mm:ss.fff"), durationMs);

        TestingLog("[POLL-EMPTY] DeviceID:{DeviceID}", deviceId);
    }

    // ── POLL NEED ACK FIRST ───────────────────────────────────────────────────
    public void LogPollNeedAck(
        string typeMid, decimal deviceId, decimal deviceType,
        DateTime reqTime, long durationMs)
    {
        _info.Warning(
            "[POLL]\n" +
            "   [REQ] DeviceID:{DeviceID}  Type:{DeviceType}\n" +
            "   [RES] BLOCKED — previous batch not yet confirmed, ACK required first  Duration:{Duration}ms",
            deviceId, deviceType, durationMs);

        _debug.Warning(
            "[POLL]\n" +
            "   [REQ] DeviceID:{DeviceID}  Type:{DeviceType}  ReqTime:{ReqTime}\n" +
            "   [RES] BLOCKED — previous batch not yet confirmed, ACK required first  Duration:{Duration}ms",
            deviceId, deviceType,
            reqTime.ToString("HH:mm:ss.fff"), durationMs);

        TestingLog("[POLL-BLOCKED] DeviceID:{DeviceID}", deviceId);
    }

    // ── ACK RECEIVED ──────────────────────────────────────────────────────────
    public void LogAck(
        string typeMid, decimal deviceId, decimal deviceType,
        List<decimal> clientIds, AckResult result,
        DateTime t2, long serverMs,
        double upstreamMs, double downstreamMsPrev, double fullRoundTripPrev,
        int ackWarnSeconds)
    {
        var claimed  = clientIds.Count;
        var firstId  = clientIds.First();
        var lastId   = clientIds.Last();
        var idRange  = claimed == 1 ? $"{firstId}" : $"{firstId}-{lastId}";

        var avgDelay = result.AckDelays.Count > 0
            ? Math.Round(result.AckDelays.Values.Average(), 2) : 0.0;
        var maxDelay = result.AckDelays.Count > 0
            ? result.AckDelays.Values.Max() : 0.0;

        var upLabel = upstreamMs        >= 0 ? $"{upstreamMs}ms"        : "N/A";
        var rtLabel = fullRoundTripPrev >= 0 ? $"{fullRoundTripPrev}ms" : "N/A";

        _info.Information(
            "[ACK]\n" +
            "   [REQ] DeviceID:{DeviceID}  Type:{DeviceType}  Confirming:{Claimed} messages  IDs:[{IDs}]\n" +
            "   [RES] Confirmed:{Updated}  AvgDelay:{Avg}ms  MaxDelay:{Max}ms  ServerMs:{Server}ms  NetworkMs:{Up}",
            deviceId, deviceType, claimed, idRange,
            result.UpdatedCount, avgDelay, maxDelay, serverMs, upLabel);

        _debug.Information(
            "[ACK]\n" +
            "   [REQ] DeviceID:{DeviceID}  Type:{DeviceType}  Confirming:{Claimed} messages  IDs:[{IDs}]  T2:{T2}\n" +
            "   [RES] Confirmed:{Updated}  AvgDelay:{Avg}ms  MaxDelay:{Max}ms  ServerMs:{Server}ms  NetworkMs:{Up}  RoundTrip:{RT}",
            deviceId, deviceType, claimed, idRange,
            t2.ToString("HH:mm:ss.fff"),
            result.UpdatedCount, avgDelay, maxDelay, serverMs, upLabel, rtLabel);

        // Warnings and errors
        if (maxDelay > ackWarnSeconds)
            _error.Warning(
                "[ACK-SLOW] DeviceID:{DeviceID}  Type:{DeviceType}  MaxDelay:{Max}ms  Threshold:{Threshold}s — device took too long to confirm",
                deviceId, deviceType, maxDelay, ackWarnSeconds);

        if (result.UpdatedCount == 0 && clientIds.Count > 0)
            _error.Error(
                "[ACK-FAILED] DeviceID:{DeviceID}  Type:{DeviceType}  Claimed:{Claimed} messages but 0 updated — IDs may be invalid or already confirmed",
                deviceId, deviceType, claimed);

        if (result.MismatchedIds.Count > 0)
            _error.Error(
                "[ACK-MISMATCH] DeviceID:{DeviceID}  Type:{DeviceType}  {Count} IDs not found: [{Missed}]",
                deviceId, deviceType,
                result.MismatchedIds.Count,
                string.Join(",", result.MismatchedIds));

        TestingLog("[ACK] DeviceID:{DeviceID}  Confirmed:{Updated}  AvgDelay:{Avg}ms",
            deviceId, result.UpdatedCount, avgDelay);
    }

    // ── RESTORE ───────────────────────────────────────────────────────────────
    public void LogRestore(
        string typeMid, decimal deviceId, decimal deviceType,
        int restoredCount, DateTime reqTime, long durationMs)
    {
        _info.Warning(
            "[RESTORE]\n" +
            "   [REQ] Device reconnected  DeviceID:{DeviceID}  Type:{DeviceType}\n" +
            "   [RES] {Count} unconfirmed messages reset and ready to resend  Duration:{Duration}ms",
            deviceId, deviceType, restoredCount, durationMs);

        _debug.Warning(
            "[RESTORE]\n" +
            "   [REQ] Device reconnected  DeviceID:{DeviceID}  Type:{DeviceType}  ReqTime:{ReqTime}\n" +
            "   [RES] {Count} unconfirmed messages reset and ready to resend  Duration:{Duration}ms",
            deviceId, deviceType,
            reqTime.ToString("HH:mm:ss.fff"),
            restoredCount, durationMs);
    }

    // ── REFRESH ───────────────────────────────────────────────────────────────
    public void LogRefresh(
        string typeMid, decimal deviceId, decimal deviceType,
        bool success, long durationMs)
    {
        if (success)
        {
            _info.Information(
                "[REFRESH]\n" +
                "   [REQ] DeviceID:{DeviceID}  Type:{DeviceType}\n" +
                "   [RES] Token renewed  Duration:{Duration}ms",
                deviceId, deviceType, durationMs);

            _debug.Information(
                "[REFRESH]\n" +
                "   [REQ] DeviceID:{DeviceID}  Type:{DeviceType}\n" +
                "   [RES] Token renewed  Duration:{Duration}ms",
                deviceId, deviceType, durationMs);
        }
        else
        {
            _info.Warning(
                "[REFRESH]\n" +
                "   [REQ] DeviceID:{DeviceID}  Type:{DeviceType}\n" +
                "   [RES] FAILED  Duration:{Duration}ms",
                deviceId, deviceType, durationMs);

            _error.Warning(
                "[REFRESH-FAIL] DeviceID:{DeviceID}  Type:{DeviceType}  Token renewal failed",
                deviceId, deviceType);
        }
    }

    // ── EVENT ─────────────────────────────────────────────────────────────────
    public void LogBulkEvent(
        string typeMid, decimal deviceId, decimal? deviceType,
        int count, DateTime reqTime, long serverMs,
        DateTime? t1, DateTime t2, DateTime t3,
        string message = "" , decimal eventSeqNo = 0)
    {
        double upstreamMs = t1.HasValue
            ? Math.Round((t2 - t1.Value).TotalMilliseconds, 1) : -1;
        double fullMs = t1.HasValue
            ? Math.Round((t3 - t1.Value).TotalMilliseconds, 1) : -1;

        var upLabel   = upstreamMs >= 0 ? $"{upstreamMs}ms" : "N/A";
        var fullLabel = fullMs     >= 0 ? $"{fullMs}ms"     : "N/A";

        _info.Information(
            "[EVENT]\n" +
            "   [REQ] DeviceID:{DeviceID}  Type:{DeviceType}  Message:{Message}\n" +
            "   [RES] Stored  ServerMs:{Server}ms  NetworkMs:{Up}  TotalMs:{Full}  eventSeqNo:{eventSeqNo}",
            deviceId, deviceType, message,
            serverMs, upLabel, fullLabel, eventSeqNo);

        _debug.Information(
            "[EVENT]\n" +
            "   [REQ] DeviceID:{DeviceID}  Type:{DeviceType}  Message:{Message}  ReqTime:{ReqTime}  T1:{T1}  T2:{T2}\n" +
            "   [RES] Stored  ServerMs:{Server}ms  NetworkMs:{Up}  TotalMs:{Full}  T3:{T3} eventSeqNo:{eventSeqNo}",
            deviceId, deviceType, message,
            reqTime.ToString("HH:mm:ss.fff"),
            t1?.ToString("HH:mm:ss.fff") ?? "N/A",
            t2.ToString("HH:mm:ss.fff"),
            serverMs, upLabel, fullLabel,
            t3.ToString("HH:mm:ss.fff"));

        TestingLog("[EVENT] DeviceID:{DeviceID}  Message:{Message}  ServerMs:{Server}ms",
            deviceId, message, serverMs);
    }

    // ── STALL RECOVERY ────────────────────────────────────────────────────────
    public void LogStallRecovery(List<StalledGroup> groups)
    {
        if (groups.Count == 0)
        {
            _debug.Information("[STALL-CHECK] All devices are up to date — no stuck messages");
            return;
        }

        foreach (var g in groups)
        {
            _info.Warning(
                "[STALL] Device:{TypeMID} — {Total} messages stuck  Reset:{Reset}  PermanentlyFailed:{Failed}  MaxRetries:{MaxRetry}",
                g.TypeMID, g.RowCount, g.ResetCount, g.FailedCount, g.MaxRetry);

            _error.Warning(
                "[STALL-DEVICE] Device:{TypeMID} — {Total} messages not confirmed in time  Reset:{Reset}  PermanentlyFailed:{Failed}  MaxRetries:{MaxRetry}",
                g.TypeMID, g.RowCount, g.ResetCount, g.FailedCount, g.MaxRetry);

            _debug.Warning(
                "[STALL] Device:{TypeMID}  StalledRows:{Total}  Reset:{Reset}  Failed:{Failed}  MaxRetry:{MaxRetry}",
                g.TypeMID, g.RowCount, g.ResetCount, g.FailedCount, g.MaxRetry);
        }
    }

    // ── EXCEPTION / DB ERROR ──────────────────────────────────────────────────
    public void LogException(string action, string typeMid, decimal deviceId, Exception ex)
    {
        _error.Error(ex,
            "[ERROR] Action:{Action}  DeviceID:{DeviceID}  Problem:{Msg}",
            action, deviceId, ex.Message);

        TestingLog("[EXCEPTION] Action:{Action}  Error:{Msg}", action, ex.Message);
    }

    public void LogDbFailure(string action, string typeMid, Exception ex)
    {
        _error.Error(ex,
            "[DB-ERROR] Action:{Action}  Problem:{Msg}",
            action, ex.Message);

        TestingLog("[DB-ERROR] Action:{Action}  Error:{Msg}", action, ex.Message);
    }

    // ── TESTING INTERNAL STEPS ────────────────────────────────────────────────
    public void LogTestingStep(string step, params object?[] args)
    {
        TestingLog(step, args);
    }

    // ── ACK TIMING ────────────────────────────────────────────────────────────
    public void LogAckTiming(string typeMid, decimal deviceId,
        long serverMs, double roundTripMs, double clientMs)
    {
        _debug.Information(
            "[ACK-TIMING] DeviceID:{DeviceID}  ServerMs:{Server}ms  RoundTripMs:{RoundTrip}ms  ClientMs:{Client}ms",
            deviceId, serverMs,
            Math.Round(roundTripMs, 1),
            Math.Round(clientMs, 1));
    }

    // ── Private helper ────────────────────────────────────────────────────────
    private void TestingLog(string template, params object?[] args)
    {
        if (!_testingEnabled) return;
        _testing.Debug(template, args);
    }
}