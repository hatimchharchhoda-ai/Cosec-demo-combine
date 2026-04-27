using MatPoll.Models;
using Serilog;

namespace MatPoll.Services;

// ─────────────────────────────────────────────────────────────────────────────
// ActivityLogger — human-readable logs, no TypeMID shown
//
// Every log shows:
//   [REQ] → when request arrived (ReqTime)
//   [RES] → when response was sent (ResTime) + how long it took (Duration)
//
// Log files:
//   info.log    → clean summary: one REQ+RES block per operation
//   debug.log   → same + T1/T2/T3 timing details
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
        decimal deviceId, string deviceName,
        decimal deviceType, bool success, string detail, long durationMs,
        string mac = "", string ip = "",
        DateTime? reqTime = null)
    {
        var req = (reqTime ?? DateTime.UtcNow).ToString("HH:mm:ss.fff");
        var res = DateTime.UtcNow.ToString("HH:mm:ss.fff");

        if (success)
        {
            _info.Information(
                "[LOGIN]\n" +
                "   [REQ] {ReqTime}  Device:{Name}  Type:{DeviceType}  MAC:{MAC}  IP:{IP}\n" +
                "   [RES] {ResTime}  SUCCESS  DeviceID:{DeviceID}  Duration:{Duration}ms",
                req, deviceName, deviceType, mac, ip,
                res, deviceId, durationMs);

            _debug.Information(
                "[LOGIN]\n" +
                "   [REQ] {ReqTime}  Device:{Name}  Type:{DeviceType}  MAC:{MAC}  IP:{IP}\n" +
                "   [RES] {ResTime}  SUCCESS  DeviceID:{DeviceID}  Duration:{Duration}ms",
                req, deviceName, deviceType, mac, ip,
                res, deviceId, durationMs);
        }
        else
        {
            _info.Warning(
                "[LOGIN]\n" +
                "   [REQ] {ReqTime}  Device:{Name}  Type:{DeviceType}  MAC:{MAC}  IP:{IP}\n" +
                "   [RES] {ResTime}  FAILED  Reason:{Reason}  Duration:{Duration}ms",
                req, deviceName, deviceType, mac, ip,
                res, detail, durationMs);

            _debug.Warning(
                "[LOGIN]\n" +
                "   [REQ] {ReqTime}  Device:{Name}  Type:{DeviceType}  MAC:{MAC}  IP:{IP}\n" +
                "   [RES] {ResTime}  FAILED  Reason:{Reason}  Duration:{Duration}ms",
                req, deviceName, deviceType, mac, ip,
                res, detail, durationMs);

            _error.Warning(
                "[LOGIN-FAIL] ReqTime:{ReqTime}  Device:{Name}  Type:{DeviceType}  Reason:{Reason}",
                req, deviceName, deviceType, detail);
        }
    }

    // ── POLL DATA SENT ────────────────────────────────────────────────────────
    // public void LogPollDataSent(
    //     string typeMid, decimal deviceId, string deviceName, decimal deviceType,
    //     List<MatCommTrn> rows,
    //     DateTime reqTime, long durationMs)
    // {
    //     var rowCount   = rows.Count;
    //     var firstId    = rows.First().TrnID;
    //     var lastId     = rows.Last().TrnID;
    //     var idRange    = rowCount == 1 ? $"{firstId}" : $"{firstId}-{lastId}";
    //     var firstMsg   = rows.First().MsgStr ?? "";
    //     var lastMsg    = rows.Last().MsgStr  ?? "";
    //     var msgSummary = rowCount == 1 ? $"{firstMsg}" : $"{firstMsg} .. {lastMsg}";

    //     var req = reqTime.ToString("HH:mm:ss.fff");
    //     var res = DateTime.UtcNow.ToString("HH:mm:ss.fff");

    //     _info.Information(
    //         "[POLL]\n" +
    //         "   [REQ] {ReqTime}  Device:{Name}  DeviceID:{DeviceID}  Type:{DeviceType}\n" +
    //         "   [RES] {ResTime}  Sent:{Sent}  Messages:[{Messages}]  IDs:[{IDs}]  Duration:{Duration}ms",
    //         req, deviceName, deviceId, deviceType,
    //         res, rowCount, msgSummary, idRange, durationMs);

    //     _debug.Information(
    //         "[POLL]\n" +
    //         "   [REQ] {ReqTime}  Device:{Name}  DeviceID:{DeviceID}  Type:{DeviceType}\n" +
    //         "   [RES] {ResTime}  Sent:{Sent}  Messages:[{Messages}]  IDs:[{IDs}]  Duration:{Duration}ms",
    //         req, deviceName, deviceId, deviceType,
    //         res, rowCount, msgSummary, idRange, durationMs);

    //     TestingLog("[POLL] Device:{Name}  DeviceID:{DeviceID}  Sent:{Sent}  IDs:[{IDs}]  ReqTime:{ReqTime}  ResTime:{ResTime}",
    //         deviceName, deviceId, rowCount, idRange, req, res);
    // }


    public void LogPollDataSent(
    decimal deviceId, decimal deviceType,
    List<MatCommTrn> rows,
    DateTime reqTime, long durationMs)
{
    var rowCount = rows.Count;
    var firstId  = rows.First().TrnID;
    var lastId   = rows.Last().TrnID;
    var idRange  = rowCount == 1 ? $"{firstId}" : $"{firstId}-{lastId}";

    // build ID:Message for every row
    var idMsgLines = string.Join("\n              ", 
        rows.Select(r => $"{r.TrnID}:{r.MsgStr ?? ""}"));

    var req = reqTime.ToString("HH:mm:ss.fff");
    var res = DateTime.UtcNow.ToString("HH:mm:ss.fff");

    _info.Information(
        "[POLL]\n" +
        "   [REQ] {ReqTime}  DeviceID:{DeviceID}  Type:{DeviceType}\n" +
        "   [RES] {ResTime}  Sent:{Sent}  IDs:[{IDs}]  Duration:{Duration}ms\n" +
        "   [MSG] {IdMsgLines}",
        req, deviceId, deviceType,
        res, rowCount, idRange, durationMs,
        idMsgLines);

    _debug.Information(
        "[POLL]\n" +
        "   [REQ] {ReqTime}  DeviceID:{DeviceID}  Type:{DeviceType}\n" +
        "   [RES] {ResTime}  Sent:{Sent}  IDs:[{IDs}]  Duration:{Duration}ms\n" +
        "   [MSG] {IdMsgLines}",
        req,  deviceId, deviceType,
        res, rowCount, idRange, durationMs,
        idMsgLines);

    TestingLog(
        "[POLL]  DeviceID:{DeviceID}  Sent:{Sent}  IDs:[{IDs}]  ReqTime:{ReqTime}  ResTime:{ResTime}",
        deviceId, rowCount, idRange, req, res);
}
    // ── POLL NO DATA ──────────────────────────────────────────────────────────
    public void LogPollNoData(
        decimal deviceId, decimal deviceType,
        DateTime reqTime, long durationMs)
    {
        var req = reqTime.ToString("HH:mm:ss.fff");
        var res = DateTime.UtcNow.ToString("HH:mm:ss.fff");

        _info.Information(
            "[POLL]\n" +
            "   [REQ] {ReqTime}  DeviceID:{DeviceID}  Type:{DeviceType}\n" +
            "   [RES] {ResTime}  No pending messages  Duration:{Duration}ms",
            req, deviceId, deviceType,
            res, durationMs);

        _debug.Information(
            "[POLL]\n" +
            "   [REQ] {ReqTime}  DeviceID:{DeviceID}  Type:{DeviceType}\n" +
            "   [RES] {ResTime}  No pending messages  Duration:{Duration}ms",
            req, deviceId, deviceType,
            res, durationMs);

        TestingLog("[POLL-EMPTY] DeviceID:{DeviceID}  ReqTime:{ReqTime}", deviceId, req);
    }

    // ── POLL NEED ACK FIRST ───────────────────────────────────────────────────
    public void LogPollNeedAck(
      decimal deviceId, decimal deviceType,
        DateTime reqTime, long durationMs)
    {
        var req = reqTime.ToString("HH:mm:ss.fff");
        var res = DateTime.UtcNow.ToString("HH:mm:ss.fff");

        _info.Warning(
            "[POLL]\n" +
            "   [REQ] {ReqTime}  DeviceID:{DeviceID}  Type:{DeviceType}\n" +
            "   [RES] {ResTime}  BLOCKED — previous batch not yet confirmed, ACK required first  Duration:{Duration}ms",
            req, deviceId, deviceType,
            res, durationMs);

        _debug.Warning(
            "[POLL]\n" +
            "   [REQ] {ReqTime}  DeviceID:{DeviceID}  Type:{DeviceType}\n" +
            "   [RES] {ResTime}  BLOCKED — previous batch not yet confirmed, ACK required first  Duration:{Duration}ms",
            req, deviceId, deviceType,
            res, durationMs);

        TestingLog("[POLL-BLOCKED] DeviceID:{DeviceID}  ReqTime:{ReqTime}", deviceId, req);
    }

    // ── ACK RECEIVED ──────────────────────────────────────────────────────────
    public void LogAck(
         decimal deviceId, decimal deviceType,
        List<decimal> clientIds, AckResult result,
        DateTime t2, long serverMs,
        double upstreamMs, double downstreamMsPrev, double fullRoundTripPrev,
        int ackWarnSeconds)
    {
        var claimed  = clientIds.Count;
        // var firstId  = clientIds.First();
        // var lastId   = clientIds.Last();
        // var ids  = claimed == 1 ? $"{firstId}" : $"{firstId}-{lastId}";
        var idRange = string.Join(", ", clientIds);

        var avgDelay = result.AckDelays.Count > 0
            ? Math.Round(result.AckDelays.Values.Average(), 2) : 0.0;
        var maxDelay = result.AckDelays.Count > 0
            ? result.AckDelays.Values.Max() : 0.0;

        var upLabel = upstreamMs        >= 0 ? $"{upstreamMs}ms"        : "N/A";
        var rtLabel = fullRoundTripPrev >= 0 ? $"{fullRoundTripPrev}ms" : "N/A";

        // t2 = when request arrived, res = now = when response sent
        var req = t2.ToString("HH:mm:ss.fff");
        var res = DateTime.UtcNow.ToString("HH:mm:ss.fff");

        _info.Information(
            "[ACK]\n" +
            "   [REQ] {ReqTime}  DeviceID:{DeviceID}  Type:{DeviceType}  Confirming:{Claimed} messages  IDs:[{IDs}]\n" +
            "   [RES] {ResTime}  Confirmed:{Updated}  AvgDelay:{Avg}ms  MaxDelay:{Max}ms  ServerMs:{Server}ms  NetworkMs:{Up}",
            req, deviceId, deviceType, claimed, idRange,
            res, result.UpdatedCount, avgDelay, maxDelay, serverMs, upLabel);

        _debug.Information(
            "[ACK]\n" +
            "   [REQ] {ReqTime}  DeviceID:{DeviceID}  Type:{DeviceType}  Confirming:{Claimed} messages  IDs:[{IDs}]\n" +
            "   [RES] {ResTime}  Confirmed:{Updated}  AvgDelay:{Avg}ms  MaxDelay:{Max}ms  ServerMs:{Server}ms  NetworkMs:{Up}  RoundTrip:{RT}",
            req, deviceId, deviceType, claimed, idRange,
            res, result.UpdatedCount, avgDelay, maxDelay, serverMs, upLabel, rtLabel);

        // Error cases
        if (maxDelay > ackWarnSeconds)
            _error.Warning(
                "[ACK-SLOW] ReqTime:{ReqTime}  DeviceID:{DeviceID}  Type:{DeviceType}  MaxDelay:{Max}ms  Threshold:{Threshold}s — device took too long to confirm",
                req, deviceId, deviceType, maxDelay, ackWarnSeconds);

        if (result.UpdatedCount == 0 && clientIds.Count > 0)
            _error.Error(
                "[ACK-FAILED] ReqTime:{ReqTime}  DeviceID:{DeviceID}  Type:{DeviceType}  Claimed:{Claimed} messages but 0 updated — IDs may be invalid or already confirmed",
                req, deviceId, deviceType, claimed);

        if (result.MismatchedIds.Count > 0)
            _error.Error(
                "[ACK-MISMATCH] ReqTime:{ReqTime}  DeviceID:{DeviceID}  Type:{DeviceType}  {Count} IDs not found: [{Missed}]",
                req, deviceId, deviceType,
                result.MismatchedIds.Count,
                string.Join(",", result.MismatchedIds));

        TestingLog("[ACK] DeviceID:{DeviceID}  Confirmed:{Updated}  AvgDelay:{Avg}ms  ReqTime:{ReqTime}  ResTime:{ResTime}",
            deviceId, result.UpdatedCount, avgDelay, req, res);
    }

    // ── RESTORE ───────────────────────────────────────────────────────────────
    public void LogRestore(
         decimal deviceId, decimal deviceType,
        int restoredCount, DateTime reqTime, long durationMs)
    {
        var req = reqTime.ToString("HH:mm:ss.fff");
        var res = DateTime.UtcNow.ToString("HH:mm:ss.fff");

        _info.Warning(
            "[RESTORE]\n" +
            "   [REQ] {ReqTime}  Device reconnected  DeviceID:{DeviceID}  Type:{DeviceType}\n" +
            "   [RES] {ResTime}  {Count} unconfirmed messages reset and ready to resend  Duration:{Duration}ms",
            req, deviceId, deviceType,
            res, restoredCount, durationMs);

        _debug.Warning(
            "[RESTORE]\n" +
            "   [REQ] {ReqTime}  Device reconnected  DeviceID:{DeviceID}  Type:{DeviceType}\n" +
            "   [RES] {ResTime}  {Count} unconfirmed messages reset and ready to resend  Duration:{Duration}ms",
            req, deviceId, deviceType,
            res, restoredCount, durationMs);
    }

    // ── REFRESH ───────────────────────────────────────────────────────────────
    public void LogRefresh(
         decimal deviceId, decimal deviceType,
        bool success, long durationMs,
        DateTime? reqTime = null)
    {
        var req = (reqTime ?? DateTime.UtcNow).ToString("HH:mm:ss.fff");
        var res = DateTime.UtcNow.ToString("HH:mm:ss.fff");

        if (success)
        {
            _info.Information(
                "[REFRESH]\n" +
                "   [REQ] {ReqTime}  DeviceID:{DeviceID}  Type:{DeviceType}\n" +
                "   [RES] {ResTime}  Token renewed  Duration:{Duration}ms",
                req, deviceId, deviceType,
                res, durationMs);

            _debug.Information(
                "[REFRESH]\n" +
                "   [REQ] {ReqTime}  DeviceID:{DeviceID}  Type:{DeviceType}\n" +
                "   [RES] {ResTime}  Token renewed  Duration:{Duration}ms",
                req, deviceId, deviceType,
                res, durationMs);
        }
        else
        {
            _info.Warning(
                "[REFRESH]\n" +
                "   [REQ] {ReqTime}  DeviceID:{DeviceID}  Type:{DeviceType}\n" +
                "   [RES] {ResTime}  FAILED  Duration:{Duration}ms",
                req, deviceId, deviceType,
                res, durationMs);

            _error.Warning(
                "[REFRESH-FAIL] ReqTime:{ReqTime}  DeviceID:{DeviceID}  Type:{DeviceType}  Token renewal failed",
                req, deviceId, deviceType);
        }
    }

    // ── EVENT ─────────────────────────────────────────────────────────────────
    public void LogBulkEvent(
       decimal deviceId, decimal? deviceType,
        int count, DateTime reqTime, long serverMs,
        DateTime? t1, DateTime t2, DateTime t3,
        string message = "", decimal eventSeqNo = 0)
    {
        double upstreamMs = t1.HasValue
            ? Math.Round((t2 - t1.Value).TotalMilliseconds, 1) : -1;
        double fullMs = t1.HasValue
            ? Math.Round((t3 - t1.Value).TotalMilliseconds, 1) : -1;

        var upLabel   = upstreamMs >= 0 ? $"{upstreamMs}ms" : "N/A";
        var fullLabel = fullMs     >= 0 ? $"{fullMs}ms"     : "N/A";

        // reqTime = when request arrived, t3 = when response sent
        var req = reqTime.ToString("HH:mm:ss.fff");
        var res = t3.ToString("HH:mm:ss.fff");

        _info.Information(
            "[EVENT]\n" +
            "   [REQ] {ReqTime}  DeviceID:{DeviceID}  Type:{DeviceType}  SeqNo:{SeqNo}  Message:{Message}\n" +
            "   [RES] {ResTime}  Stored   SeqNo:{SeqNo} ServerMs:{Server}ms  NetworkMs:{Up}  TotalMs:{Full}",
            req, deviceId, deviceType, eventSeqNo, message,
            res,eventSeqNo ,serverMs, upLabel, fullLabel);

        _debug.Information(
            "[EVENT]\n" +
            "   [REQ] {ReqTime}  DeviceID:{DeviceID}  Type:{DeviceType}  SeqNo:{SeqNo}  Message:{Message}  T1:{T1}  T2:{T2}\n" +
            "   [RES] {ResTime}  Stored SeqNo:{SeqNo} ServerMs:{Server}ms  NetworkMs:{Up}  TotalMs:{Full}  T3:{T3}",
            req, deviceId, deviceType, eventSeqNo, message,
            t1?.ToString("HH:mm:ss.fff") ?? "N/A",
            t2.ToString("HH:mm:ss.fff"),
            res, eventSeqNo ,serverMs, upLabel, fullLabel,
            t3.ToString("HH:mm:ss.fff"));

        TestingLog("[EVENT] DeviceID:{DeviceID}  SeqNo:{SeqNo}  Message:{Message}  ReqTime:{ReqTime}  ResTime:{ResTime}  ServerMs:{Server}ms",
            deviceId, eventSeqNo, message, req, res, serverMs);
    }

    // ── STALL RECOVERY ────────────────────────────────────────────────────────
    public void LogStallRecovery(List<StalledGroup> groups)
    {
        if (groups.Count == 0)
        {
            _debug.Information("[STALL-CHECK] {Time}  All devices up to date — no stuck messages",
                DateTime.UtcNow.ToString("HH:mm:ss.fff"));
            return;
        }

        foreach (var g in groups)
        {
            _info.Warning(
                "[STALL] {Time}  Device:{DeviceID} — {Total} messages stuck  Reset:{Reset}  PermanentlyFailed:{Failed}  MaxRetries:{MaxRetry}",
                DateTime.UtcNow.ToString("HH:mm:ss.fff"),
                g.DeviceID, g.RowCount, g.ResetCount, g.FailedCount, g.MaxRetry);

            _error.Warning(
                "[STALL-DEVICE] {Time}  Device:{DeviceID} — {Total} messages not confirmed in time  Reset:{Reset}  PermanentlyFailed:{Failed}  MaxRetries:{MaxRetry}",
                DateTime.UtcNow.ToString("HH:mm:ss.fff"),
                g.DeviceID, g.RowCount, g.ResetCount, g.FailedCount, g.MaxRetry);

            _debug.Warning(
                "[STALL] {Time}  Device:{DeviceID}  StalledRows:{Total}  Reset:{Reset}  Failed:{Failed}  MaxRetry:{MaxRetry}",
                DateTime.UtcNow.ToString("HH:mm:ss.fff"),
                g.DeviceID, g.RowCount, g.ResetCount, g.FailedCount, g.MaxRetry);
        }
    }

    // ── EXCEPTION / DB ERROR ──────────────────────────────────────────────────
    public void LogException(string action,  decimal deviceId, Exception ex)
    {
        _error.Error(ex,
            "[ERROR] {Time}  Action:{Action}  DeviceID:{DeviceID}  Problem:{Msg}",
            DateTime.UtcNow.ToString("HH:mm:ss.fff"),
            action, deviceId, ex.Message);

        TestingLog("[EXCEPTION] Action:{Action}  Error:{Msg}", action, ex.Message);
    }

    public void LogDbFailure(string action, decimal ?DeviceID, Exception ex)
    {
        _error.Error(ex,
            "[DB-ERROR] {Time}  Action:{Action}  Problem:{Msg}",
            DateTime.UtcNow.ToString("HH:mm:ss.fff"),
            action, DeviceID, ex.Message);

        TestingLog("[DB-ERROR] Action:{Action}  Error:{Msg}", action, ex.Message);
    }

    // ── TESTING INTERNAL STEPS ────────────────────────────────────────────────
    public void LogTestingStep(string step, params object?[] args)
    {
        TestingLog(step, args);
    }

    // ── ACK TIMING ────────────────────────────────────────────────────────────
    public void LogAckTiming(decimal DeviceID, decimal deviceId,
        long serverMs, double roundTripMs, double clientMs)
    {
        _debug.Information(
            "[ACK-TIMING] {Time}  DeviceID:{DeviceID}  ServerMs:{Server}ms  RoundTripMs:{RoundTrip}ms  ClientMs:{Client}ms",
            DateTime.UtcNow.ToString("HH:mm:ss.fff"),
            DeviceID, serverMs,
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