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
    //
    // TrnStat state machine:
    //   0 = Pending      → Poll picks these up, flips to 1, sends to client
    //   1 = Dispatched   → Already sent, waiting for ACK. Poll returns NeedAckFirst.
    //   2 = Acknowledged → Client confirmed. Done forever. Never sent again.
    //   9 = Failed       → Too many retries. Ignored.
    [HttpGet]
    public async Task<IActionResult> Poll()
    {
        var reqTime  = DateTime.UtcNow;
        var sw       = Stopwatch.StartNew();
        var deviceId = TokenService.GetDeviceId(User);
        var typeMid  = TokenService.GetTypeMid(User);

        if (string.IsNullOrEmpty(typeMid))
            return Unauthorized();

        // Step 1: Any TrnStat=1 rows for this device in DB?
        var hasDispatched = await _repo.HasDispatchedRowsAsync(typeMid);

        if (hasDispatched)
        {
            var total = await _repo.CountPendingAsync();

            _actLog.LogPoll(typeMid, deviceId,
                hasData: false, needAckFirst: true,
                rowsSent: 0, totalPending: total,
                reqTime, sw.ElapsedMilliseconds);

            return Ok(new PollResponse
            {
                HasData      = false,
                NeedAckFirst = true,
                TotalPending = total,
                TypeMID      = typeMid,
                Rows         = new List<TrnRow>()
            });
        }

        // Step 2: No in-flight rows. Fetch fresh TrnStat=0 rows.
        var bunchSize = int.Parse(_config["PollingSettings:BunchSize"] ?? "50");
        var rows      = await _repo.FetchAndMarkDispatchedAsync(typeMid, bunchSize);
        var pending   = await _repo.CountPendingAsync();

        if (rows.Count == 0)
        {
            _actLog.LogPoll(typeMid, deviceId,
                false, false, 0, pending, reqTime, sw.ElapsedMilliseconds);

            return Ok(new PollResponse
            {
                HasData      = false,
                NeedAckFirst = false,
                TotalPending = pending,
                TypeMID      = typeMid,
                Rows         = new List<TrnRow>()
            });
        }

        // Fresh rows fetched — log with actual TrnIDs (clean format)
        var trnIds = rows.Select(r => r.TrnID).ToList();
        _actLog.LogPollWithIds(typeMid, deviceId, trnIds, pending, sw.ElapsedMilliseconds);

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

    // ── POST /api/poll/ack ────────────────────────────────────────────────────
    // Client processed the rows. Mark them TrnStat=2 (done forever).
    // Now also accepts optional Message + Header from client.
    [HttpPost("ack")]
    public async Task<IActionResult> Ack([FromBody] AckRequest req)
    {
        var reqTime  = DateTime.UtcNow;
        var sw       = Stopwatch.StartNew();
        var deviceId = TokenService.GetDeviceId(User);
        var typeMid  = TokenService.GetTypeMid(User);

        if (string.IsNullOrEmpty(typeMid))
            return Unauthorized();

        // Only marks rows that are TrnStat=1 AND TypeMID matches this device.
        var count = await _repo.MarkAcknowledgedAsync(req.TrnIDs, typeMid);

        // Log ACK — pass Message + Header if client sent them
        _actLog.LogAck(typeMid, deviceId, req.TrnIDs, count,
            true, req.Message,  reqTime, sw.ElapsedMilliseconds);

        return Ok(new AckResponse
        {
            Success      = true,
            Message      = $"{count} rows marked as acknowledged (TrnStat=2).",
            UpdatedCount = count
        });
    }

    // ── POST /api/poll/restore ────────────────────────────────────────────────
    // Use when: device crashed and lost its TrnStat=1 rows from memory.
    // Resets TrnStat=1 → TrnStat=0 for this device's TypeMID.
    [HttpPost("restore")]
    public async Task<IActionResult> Restore()
    {
        var reqTime  = DateTime.Now;
        var sw       = Stopwatch.StartNew();
        var deviceId = TokenService.GetDeviceId(User);
        var typeMid  = TokenService.GetTypeMid(User);

        if (string.IsNullOrEmpty(typeMid))
            return Unauthorized();

        var count = await _repo.RestoreDispatchedAsync(typeMid);

        _actLog.LogRestore(typeMid, deviceId, count, reqTime, sw.ElapsedMilliseconds);

        return Ok(new RestoreResponse
        {
            Success       = true,
            Message       = $"{count} rows restored to TrnStat=0. Poll again to receive them.",
            RestoredCount = count,
            TypeMID       = typeMid
        });
    }

    // for reciving events from client 
    [HttpPost("event")]
    public IActionResult ReceiveEvent([FromBody] object eventData)
    {
        var reqTime  = DateTime.Now;
        var sw       = Stopwatch.StartNew();
        var deviceId = TokenService.GetDeviceId(User);
        var typeMid  = TokenService.GetTypeMid(User);

        if (string.IsNullOrEmpty(typeMid))
            return Unauthorized();
        _actLog.LogEvent(typeMid, deviceId, eventData.ToString() ?? "No event data", reqTime, sw.ElapsedMilliseconds);

        // Respond with success
        return Ok(new { Success = true, Message = "Event received." });
    }   
}