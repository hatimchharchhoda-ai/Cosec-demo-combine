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
        var reqTime  = DateTime.UtcNow;
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
                    Rows         = new List<TrnRow>()
                });
            }

            // Step 2: Fetch fresh TrnStat=0 rows
            var bunchSize = int.Parse(_config["PollingSettings:BunchSize"] ?? "50");

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
                    Rows         = new List<TrnRow>()
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
                }).ToList()
            });
        }
        catch (Exception ex)
        {
            _actLog.LogException("POLL", typeMid, deviceId, ex);
            return StatusCode(500, new { error = "Poll failed. See error log." });
        }
    }

    // ── POST /api/poll/ack ────────────────────────────────────────────────────
    [HttpPost("ack")]
    public async Task<IActionResult> Ack([FromBody] AckRequest req)
    {
        var reqTime  = DateTime.UtcNow;
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

            _actLog.LogAck(typeMid, deviceId, req.TrnIDs,
                result, reqTime, sw.ElapsedMilliseconds, ackWarnSecs);

            return Ok(new AckResponse
            {
                Success      = true,
                Message      = $"{result.UpdatedCount} rows acknowledged (TrnStat=2).",
                UpdatedCount = result.UpdatedCount
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
        var reqTime  = DateTime.UtcNow;
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
                TypeMID       = typeMid
            });
        }
        catch (Exception ex)
        {
            _actLog.LogException("RESTORE", typeMid, deviceId, ex);
            return StatusCode(500, new { error = "Restore failed. See error log." });
        }
    }
}
