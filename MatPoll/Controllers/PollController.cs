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
    //   0 = Pending     → Poll picks these up, flips to 1, sends to client
    //   1 = Dispatched  → Already sent, waiting for ACK. Poll returns NeedAckFirst.
    //   2 = Acknowledged→ Client confirmed. Done forever. Never sent again.
    //   9 = Failed      → Too many retries. Ignored.
    //
    // RULE: Poll ONLY ever returns TrnStat=0 rows (freshly fetched).
    //       It NEVER returns TrnStat=1 rows.
    //       If TrnStat=1 rows exist → tell client to ACK first (no data sent).
    //       If client lost TrnStat=1 rows (crash) → call POST /api/poll/restore.
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
            // Do NOT send any rows.
            // Client already has them (they are TrnStat=1 = already sent).
            // Just tell the client: ACK your pending batch first.
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
                Rows         = new List<TrnRow>()  // always empty when NeedAckFirst
            });
        }

        // Step 2: No in-flight rows. Fetch fresh TrnStat=0 rows.
        // FetchAndMarkDispatchedAsync: SELECT TOP N WHERE TrnStat=0
        //                              then UPDATE TrnStat=1 in same call.
        var bunchSize = int.Parse(_config["PollingSettings:BunchSize"] ?? "50");
        var rows      = await _repo.FetchAndMarkDispatchedAsync(typeMid, bunchSize);
        var pending   = await _repo.CountPendingAsync();

        if (rows.Count == 0)
        {
            // Nothing to send right now
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

        // Fresh rows fetched (were TrnStat=0, now TrnStat=1 in DB)
        _actLog.LogPoll(typeMid, deviceId,
            true, false, rows.Count, pending, reqTime, sw.ElapsedMilliseconds);

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
        // Prevents ACKing someone else's rows or already-ACKed rows.
        var count = await _repo.MarkAcknowledgedAsync(req.TrnIDs, typeMid);

        _actLog.LogAck(typeMid, deviceId, req.TrnIDs, count,
            true, reqTime, sw.ElapsedMilliseconds);

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
    // On next poll, those rows come back as fresh TrnStat=0 → sent again.
    [HttpPost("restore")]
    public async Task<IActionResult> Restore()
    {
        var reqTime  = DateTime.UtcNow;
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
}