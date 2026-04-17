using MatPoll.DTOs;
using MatPoll.Repositories;
using MatPoll.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;

namespace MatPoll.Controllers;

[ApiController]
[Route("api/poll")]
[Authorize]
public class PollController : ControllerBase
{
    private readonly AppRepository  _repo;
    private readonly ActivityLogger _actLog;
    private readonly IConfiguration _config;

    public PollController(AppRepository repo, ActivityLogger actLog, IConfiguration config)
    {
        _repo   = repo;
        _actLog = actLog;
        _config = config;
    }

    // ── GET /api/poll ─────────────────────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> Poll()
    {
        var reqTime  = DateTime.Now;
        var sw       = Stopwatch.StartNew();
        var deviceId = TokenService.GetDeviceId(User);
        var typeMid  = TokenService.GetTypeMid(User);

        if (string.IsNullOrEmpty(typeMid))
            return Unauthorized();

        try
        {
            _actLog.LogTestingStep("[POLL-START] TypeMID:{TypeMID} DeviceID:{DeviceID}", typeMid, deviceId);

            // Step 1: TrnStat=1 rows exist for this device?
            var hasDispatched = await _repo.HasDispatchedRowsAsync(typeMid);
            if (hasDispatched)
            {
                _actLog.LogPollNeedAck(typeMid, deviceId, reqTime, sw.ElapsedMilliseconds);
                return Ok(new PollResponse
                {
                    HasData      = false,
                    NeedAckFirst = true,
                    TotalPending = await _repo.CountPendingAsync(),
                    TypeMID      = typeMid,
                    Rows         = new List<TrnRow>(),
                    ServerSentAt = DateTime.Now
                });
            }

            // Step 2: Fetch fresh TrnStat=0 rows
            var bunchSize = int.Parse(_config["PollingSettings:BunchSize"] ?? "1");

            _actLog.LogTestingStep("[POLL-FETCH] TypeMID:{TypeMID} BunchSize:{Size}", typeMid, bunchSize);

            var rows    = await _repo.FetchAndMarkDispatchedAsync(typeMid, bunchSize);
            var pending = await _repo.CountPendingAsync();

            if (rows.Count == 0)
            {
                _actLog.LogPollNoData(typeMid, deviceId, pending, reqTime, sw.ElapsedMilliseconds);
                return Ok(new PollResponse
                {
                    HasData      = false,
                    NeedAckFirst = false,
                    TotalPending = pending,
                    TypeMID      = typeMid,
                    Rows         = new List<TrnRow>(),
                    ServerSentAt = DateTime.Now
                });
            }

            // Need device name for debug log — load from DB (cached by EF)
            var device = await _repo.FindDeviceByIdAsync(deviceId);

            _actLog.LogPollDataSent(
                typeMid, deviceId, device?.DeviceName ?? "?",
                rows, pending, reqTime, sw.ElapsedMilliseconds);

            return Ok(new PollResponse
            {
                HasData      = true,
                NeedAckFirst = false,
                TotalPending = pending,
                TypeMID      = typeMid,
                Rows = rows.Select(r => new TrnRow
                {
                    TrnID    = r.TrnID,
                    MsgStr   = r.MsgStr,
                    RetryCnt = r.RetryCnt ?? 0,
                    TypeMID  = r.TypeMID
                }).ToList(),
                ServerSentAt = DateTime.Now
            });
        }
        catch (Exception ex)
        {
            _actLog.LogException("POLL", typeMid, deviceId, ex);
            return StatusCode(500, new { error = "Poll failed. See error log." });
        }
    }

    [HttpPost("ack")]
    public async Task<IActionResult> Ack([FromBody] AckRequest req)
    {
        var t2       = DateTime.Now;          // T2: server received
        var sw       = Stopwatch.StartNew();
        var deviceId = TokenService.GetDeviceId(User);
        var typeMid  = TokenService.GetTypeMid(User);

        if (string.IsNullOrEmpty(typeMid))
            return Unauthorized();

        if (req.TrnIDs == null || req.TrnIDs.Count == 0)
            return BadRequest(new { error = "TrnIDs list is empty." });

        try
        {
            _actLog.LogTestingStep(
                "[ACK-START] TypeMID:{TypeMID} DeviceID:{DeviceID} ClaimedIDs:[{IDs}]",
                typeMid, deviceId, string.Join(",", req.TrnIDs));

            var ackWarnSecs = _config.GetValue<int>("PollingSettings:AckTimeoutWarningSeconds", 30);
            var result      = await _repo.MarkAcknowledgedAsync(req.TrnIDs, typeMid);

            sw.Stop();
            var t3 = DateTime.Now;           // T3: server about to respond
            long serverMs = sw.ElapsedMilliseconds;  // T3 - T2

            // T1 provided by client (when they sent this request)
            double upstreamMs = req.T1.HasValue
                ? Math.Round((t2 - req.T1.Value).TotalMilliseconds, 1)
                : -1;                           // T2 - T1

            // T4Prev = when client received the PREVIOUS ack response
            // DownstreamMs and FullRoundTrip refer to the previous cycle
            double downstreamMsPrev  = -1;
            double fullRoundTripPrev = -1;

            if (req.T4Prev.HasValue && req.T1.HasValue)
            {
                // We need T3 of the previous request to compute downstream.
                // Since we don't store it, we approximate:
                // DownstreamPrev ≈ T4Prev - T1 - UpstreamMs - ServerMs_prev
                // 
                // Simpler and honest: log what we *can* derive exactly.
                // FullRoundTrip of previous = T4Prev - T1_prev
                // But T1_prev isn't sent. So instead:
                // Treat T4Prev as "how long ago the last response landed"
                // relative to T1 (client sent this request right after receiving last one)
                fullRoundTripPrev = Math.Round((req.T1.Value - req.T4Prev.Value).TotalMilliseconds
                    + /* upstream this req */ (upstreamMs >= 0 ? upstreamMs : 0), 1);
                // DownstreamPrev = T4Prev - (T1_prev + UpstreamMs_prev + ServerMs_prev)
                // Best we can do without T3_prev stored: log as N/A and note limitation
                downstreamMsPrev = -1; // requires T3 from previous cycle — not available
            }

            _actLog.LogTiming("ACK", typeMid, deviceId, req.T1, t2, t3);

            return Ok(new AckResponse
            {
                Success      = true,
                Message      = $"{result.UpdatedCount} rows acknowledged (TrnStat=2).",
                UpdatedCount = result.UpdatedCount,
                ServerSentAt  = DateTime.Now
            });
        }
        catch (Exception ex)
        {
            _actLog.LogException("ACK", typeMid, deviceId, ex);
            return StatusCode(500, new { error = "ACK failed. See error log." });
        }
    }

    // ── POST /api/poll/restore ────────────────────────────────────────────────
    [HttpPost("restore")]
    public async Task<IActionResult> Restore()
    {
        var reqTime  = DateTime.Now;
        var sw       = Stopwatch.StartNew();
        var deviceId = TokenService.GetDeviceId(User);
        var typeMid  = TokenService.GetTypeMid(User);

        if (string.IsNullOrEmpty(typeMid))
            return Unauthorized();

        try
        {
            _actLog.LogTestingStep(
                "[RESTORE-START] TypeMID:{TypeMID} DeviceID:{DeviceID}", typeMid, deviceId);

            var count = await _repo.RestoreDispatchedAsync(typeMid);

            _actLog.LogRestore(typeMid, deviceId, count, reqTime, sw.ElapsedMilliseconds);

            return Ok(new RestoreResponse
            {
                Success       = true,
                Message       = $"{count} rows restored to TrnStat=0.",
                RestoredCount = count,
                TypeMID       = typeMid,
                ServerSentAt  = DateTime.Now
            });
        }
        catch (Exception ex)
        {
            _actLog.LogException("RESTORE", typeMid, deviceId, ex);
            return StatusCode(500, new { error = "Restore failed. See error log." });
        }
    }

    // for reciving events from client 
    [HttpPost("event")]
    public async Task<IActionResult> ReceiveEvent([FromBody] DeviceEventDto dto)
    {
        var t2       = DateTime.Now;
        var reqTime  = DateTime.Now;
        var sw       = Stopwatch.StartNew();
        var deviceId = TokenService.GetDeviceId(User);
        var typeMid  = TokenService.GetTypeMid(User);

        if (string.IsNullOrEmpty(typeMid))
            return Unauthorized();

        await _repo.InsertDeviceEvent(dto, deviceId);

        sw.Stop();
        var t3 = DateTime.Now; 

        _actLog.LogTiming("EVENT", typeMid, deviceId, dto.T1, t2, t3);

        return Ok(new { Success = true, Message = "Event stored.", ServerSentAt = DateTime.Now });
    }
}
