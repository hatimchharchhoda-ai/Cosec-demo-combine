// using MatPoll.DTOs;
// using MatPoll.Repositories;
// using MatPoll.Services;
// using Microsoft.AspNetCore.Authorization;
// using Microsoft.AspNetCore.Mvc;
// using System.Diagnostics;

// namespace MatPoll.Controllers;

// [ApiController]
// [Route("api/poll")]
// [Authorize]
// public class PollController : ControllerBase
// {
//     private readonly AppRepository  _repo;
//     private readonly ActivityLogger _actLog;
//     private readonly IConfiguration _config;

//     public PollController(AppRepository repo, ActivityLogger actLog, IConfiguration config)
//     {
//         _repo   = repo;
//         _actLog = actLog;
//         _config = config;
//     }

//     // ── GET /api/poll ─────────────────────────────────────────────────────────
//     [HttpGet]
//     public async Task<IActionResult> Poll()
//     {
//         var reqTime    = DateTime.UtcNow;
//         var sw         = Stopwatch.StartNew();
//         var deviceId   = TokenService.GetDeviceId(User);
//         var typeMid    = TokenService.GetTypeMid(User);
//         var deviceType = TokenService.GetDeviceType(User);
//         var deviceName = TokenService.GetDeviceName(User);

//         if (string.IsNullOrEmpty(typeMid))
//             return Unauthorized();

//         try
//         {
//             _actLog.LogTestingStep("[POLL-START] TypeMID:{TypeMID} DeviceID:{DeviceID}", typeMid, deviceId);

//             // Step 1: TrnStat=1 rows exist for this device?
//             var hasDispatched = await _repo.HasDispatchedRowsAsync(typeMid);
//             if (hasDispatched)
//             {
//                 _actLog.LogPollNeedAck(typeMid, deviceId, deviceType, reqTime, sw.ElapsedMilliseconds);
//                 return Ok(new PollResponse
//                 {
//                     HasData      = false,
//                     NeedAckFirst = true,
//                     TypeMID      = typeMid,
//                     Rows         = new List<TrnRow>(),
//                     ServerSentAt = DateTime.UtcNow
//                 });
//             }

//             // Step 2: Fetch fresh TrnStat=0 rows
//             var bunchSize = int.Parse(_config["PollingSettings:BunchSize"] ?? "1");

//             _actLog.LogTestingStep("[POLL-FETCH] TypeMID:{TypeMID} BunchSize:{Size}", typeMid, bunchSize);

//             var rows = await _repo.FetchAndMarkDispatchedAsync(typeMid, bunchSize);

//             if (rows.Count == 0)
//             {
//                 _actLog.LogPollNoData(typeMid, deviceId, deviceType, reqTime, sw.ElapsedMilliseconds);
//                 return Ok(new PollResponse
//                 {
//                     HasData      = false,
//                     NeedAckFirst = false,
//                     TypeMID      = typeMid,
//                     Rows         = new List<TrnRow>(),
//                     ServerSentAt = DateTime.UtcNow
//                 });
//             }

//             // Step 3 — count pending AFTER fetch for accurate number
//             var totalPending = await _repo.CountPendingAsync(typeMid);

//             sw.Stop();

//             _actLog.LogPollDataSent(
//                 typeMid, deviceId, deviceName, deviceType,
//                 rows, totalPending,
//                 reqTime, sw.ElapsedMilliseconds);

//             return Ok(new PollResponse
//             {
//                 HasData      = true,
//                 NeedAckFirst = false,
//                 TypeMID      = typeMid,
//                 Rows = rows.Select(r => new TrnRow
//                 {
//                     TrnID    = r.TrnID,
//                     MsgStr   = r.MsgStr,
//                     RetryCnt = r.RetryCnt ?? 0,
//                     TypeMID  = r.TypeMID
//                 }).ToList(),
//                 ServerSentAt = DateTime.UtcNow
//             });
//         }
//         catch (Exception ex)
//         {
//             _actLog.LogException("POLL", typeMid, deviceId, ex);
//             return StatusCode(500, new { error = "Poll failed. See error log." });
//         }
//     }

//     // ── POST /api/poll/ack ────────────────────────────────────────────────────
//     [HttpPost("ack")]
//     public async Task<IActionResult> Ack([FromBody] AckRequest req)
//     {
//         var t2         = DateTime.UtcNow;
//         var sw         = Stopwatch.StartNew();
//         var deviceId   = TokenService.GetDeviceId(User);
//         var typeMid    = TokenService.GetTypeMid(User);
//         var deviceType = TokenService.GetDeviceType(User);

//         if (string.IsNullOrEmpty(typeMid))
//             return Unauthorized();

//         if (req.TrnIDs == null || req.TrnIDs.Count == 0)
//             return BadRequest(new { error = "TrnIDs list is empty." });

//         try
//         {
//             _actLog.LogTestingStep(
//                 "[ACK-START] TypeMID:{TypeMID} DeviceID:{DeviceID} Count:{Count}",
//                 typeMid, deviceId, req.TrnIDs.Count);

//             var ackWarnSecs = _config.GetValue<int>("PollingSettings:AckTimeoutWarningSeconds", 30);
//             var result      = await _repo.MarkAcknowledgedAsync(req.TrnIDs, typeMid);

//             sw.Stop();
//             var t3       = DateTime.UtcNow;
//             long serverMs = sw.ElapsedMilliseconds;

//             double upstreamMs = req.T1.HasValue
//                 ? Math.Round((t2 - req.T1.Value).TotalMilliseconds, 1) : -1;

//             double fullRoundTripPrev = -1;
//             if (req.T4Prev.HasValue && req.T1.HasValue && upstreamMs >= 0)
//                 fullRoundTripPrev = Math.Round(
//                     (req.T1.Value - req.T4Prev.Value).TotalMilliseconds + upstreamMs, 1);

//             _actLog.LogAck(
//                 typeMid, deviceId, deviceType,
//                 req.TrnIDs, result,
//                 t2, serverMs,
//                 upstreamMs, -1, fullRoundTripPrev,
//                 ackWarnSecs);

//             return Ok(new AckResponse
//             {
//                 Success      = true,
//                 Message      = $"{result.UpdatedCount} rows acknowledged (TrnStat=2).",
//                 UpdatedCount = result.UpdatedCount,
//                 ServerSentAt = DateTime.UtcNow
//             });
//         }
//         catch (Exception ex)
//         {
//             _actLog.LogException("ACK", typeMid, deviceId, ex);
//             return StatusCode(500, new { error = "ACK failed. See error log." });
//         }
//     }

//     // ── POST /api/poll/restore ────────────────────────────────────────────────
//     [HttpPost("restore")]
//     public async Task<IActionResult> Restore()
//     {
//         var reqTime    = DateTime.UtcNow;
//         var sw         = Stopwatch.StartNew();
//         var deviceId   = TokenService.GetDeviceId(User);
//         var typeMid    = TokenService.GetTypeMid(User);
//         var deviceType = TokenService.GetDeviceType(User);

//         if (string.IsNullOrEmpty(typeMid))
//             return Unauthorized();

//         try
//         {
//             _actLog.LogTestingStep(
//                 "[RESTORE-START] TypeMID:{TypeMID} DeviceID:{DeviceID}", typeMid, deviceId);

//             var count = await _repo.RestoreDispatchedAsync(typeMid);

//             _actLog.LogRestore(typeMid, deviceId, deviceType, count, reqTime, sw.ElapsedMilliseconds);

//             return Ok(new RestoreResponse
//             {
//                 Success       = true,
//                 Message       = $"{count} rows restored to TrnStat=0.",
//                 RestoredCount = count,
//                 TypeMID       = typeMid,
//                 ServerSentAt  = DateTime.UtcNow
//             });
//         }
//         catch (Exception ex)
//         {
//             _actLog.LogException("RESTORE", typeMid, deviceId, ex);
//             return StatusCode(500, new { error = "Restore failed. See error log." });
//         }
//     }

//     // ── POST /api/poll/events ─────────────────────────────────────────────────
//     [HttpPost("events")]
//     public async Task<IActionResult> ReceiveEvent([FromBody] DeviceEventDto dto)
//     {
//         var t2         = DateTime.UtcNow;
//         var reqTime    = DateTime.UtcNow;
//         var sw         = Stopwatch.StartNew();
//         var deviceId   = TokenService.GetDeviceId(User);
//         var typeMid    = TokenService.GetTypeMid(User);
//         var deviceType = TokenService.GetDeviceType(User);

//         if (string.IsNullOrEmpty(typeMid))        return Unauthorized();
//         if (dto is null)                          return BadRequest(new { error = "Empty event." });
//         if (string.IsNullOrEmpty(dto.Message))    return BadRequest(new { error = "Message is required." });

//         try
//         {
//             await _repo.InsertDeviceEventAsync(dto, deviceId, deviceType);

//             sw.Stop();
//             var t3 = DateTime.UtcNow;

//             _actLog.LogBulkEvent(
//                 typeMid, deviceId, deviceType,
//                 count: 1,
//                 reqTime, sw.ElapsedMilliseconds,
//                 dto.T1, t2, t3);

//             return Ok(new { Success = true, ServerSentAt = t3 });
//         }
//         catch (Exception ex)
//         {
//             _actLog.LogException("EVENT", typeMid, deviceId, ex);
//             return StatusCode(500, new { error = "Event failed. See error log." });
//         }
//     }
// }

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
        var reqTime    = DateTime.UtcNow;
        var sw         = Stopwatch.StartNew();
        var deviceId   = TokenService.GetDeviceId(User);
        var typeMid    = TokenService.GetTypeMid(User);
        var deviceType = TokenService.GetDeviceType(User);
        var deviceName = TokenService.GetDeviceName(User);

        if (string.IsNullOrEmpty(typeMid))
            return Unauthorized();

        try
        {
            _actLog.LogTestingStep("[POLL-START] DeviceID:{DeviceID}", deviceId);

            var hasDispatched = await _repo.HasDispatchedRowsAsync(typeMid);
            if (hasDispatched)
            {
                _actLog.LogPollNeedAck(typeMid, deviceId, deviceType, reqTime, sw.ElapsedMilliseconds);
                return Ok(new PollResponse
                {
                    HasData      = false,
                    NeedAckFirst = true,
                    TypeMID      = typeMid,
                    Rows         = new List<TrnRow>(),
                    ServerSentAt = DateTime.UtcNow
                });
            }

            var bunchSize = int.Parse(_config["PollingSettings:BunchSize"] ?? "1");

            _actLog.LogTestingStep("[POLL-FETCH] DeviceID:{DeviceID}  BunchSize:{Size}", deviceId, bunchSize);

            var rows = await _repo.FetchAndMarkDispatchedAsync(typeMid, bunchSize);

            if (rows.Count == 0)
            {
                _actLog.LogPollNoData(typeMid, deviceId, deviceType, reqTime, sw.ElapsedMilliseconds);
                return Ok(new PollResponse
                {
                    HasData      = false,
                    NeedAckFirst = false,
                    TypeMID      = typeMid,
                    Rows         = new List<TrnRow>(),
                    ServerSentAt = DateTime.UtcNow
                });
            }

            var totalPending = await _repo.CountPendingAsync(typeMid);

            sw.Stop();

            _actLog.LogPollDataSent(
                typeMid, deviceId, deviceName, deviceType,
                rows, totalPending,
                reqTime, sw.ElapsedMilliseconds);

            return Ok(new PollResponse
            {
                HasData      = true,
                NeedAckFirst = false,
                TypeMID      = typeMid,
                Rows = rows.Select(r => new TrnRow
                {
                    TrnID    = r.TrnID,
                    MsgStr   = r.MsgStr,
                    RetryCnt = r.RetryCnt ?? 0,
                    TypeMID  = r.TypeMID
                }).ToList(),
                ServerSentAt = DateTime.UtcNow
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
        var t2         = DateTime.UtcNow;
        var sw         = Stopwatch.StartNew();
        var deviceId   = TokenService.GetDeviceId(User);
        var typeMid    = TokenService.GetTypeMid(User);
        var deviceType = TokenService.GetDeviceType(User);

        if (string.IsNullOrEmpty(typeMid))
            return Unauthorized();

        if (req.TrnIDs == null || req.TrnIDs.Count == 0)
            return BadRequest(new { error = "TrnIDs list is empty." });

        try
        {
            _actLog.LogTestingStep(
                "[ACK-START] DeviceID:{DeviceID}  Count:{Count}",
                deviceId, req.TrnIDs.Count);

            var ackWarnSecs = _config.GetValue<int>("PollingSettings:AckTimeoutWarningSeconds", 30);
            var result      = await _repo.MarkAcknowledgedAsync(req.TrnIDs, typeMid);

            sw.Stop();
            long serverMs = sw.ElapsedMilliseconds;

            double upstreamMs = req.T1.HasValue
                ? Math.Round((t2 - req.T1.Value).TotalMilliseconds, 1) : -1;

            double fullRoundTripPrev = -1;
            if (req.T4Prev.HasValue && req.T1.HasValue && upstreamMs >= 0)
                fullRoundTripPrev = Math.Round(
                    (req.T1.Value - req.T4Prev.Value).TotalMilliseconds + upstreamMs, 1);

            _actLog.LogAck(
                typeMid, deviceId, deviceType,
                req.TrnIDs, result,
                t2, serverMs,
                upstreamMs, -1, fullRoundTripPrev,
                ackWarnSecs);

            return Ok(new AckResponse
            {
                Success      = true,
                Message      = $"{result.UpdatedCount} rows acknowledged (TrnStat=2).",
                UpdatedCount = result.UpdatedCount,
                ServerSentAt = DateTime.UtcNow
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
        var reqTime    = DateTime.UtcNow;
        var sw         = Stopwatch.StartNew();
        var deviceId   = TokenService.GetDeviceId(User);
        var typeMid    = TokenService.GetTypeMid(User);
        var deviceType = TokenService.GetDeviceType(User);

        if (string.IsNullOrEmpty(typeMid))
            return Unauthorized();

        try
        {
            _actLog.LogTestingStep("[RESTORE-START] DeviceID:{DeviceID}", deviceId);

            var count = await _repo.RestoreDispatchedAsync(typeMid);

            _actLog.LogRestore(typeMid, deviceId, deviceType, count, reqTime, sw.ElapsedMilliseconds);

            return Ok(new RestoreResponse
            {
                Success       = true,
                Message       = $"{count} rows restored to TrnStat=0.",
                RestoredCount = count,
                TypeMID       = typeMid,
                ServerSentAt  = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _actLog.LogException("RESTORE", typeMid, deviceId, ex);
            return StatusCode(500, new { error = "Restore failed. See error log." });
        }
    }

    // ── POST /api/poll/events ─────────────────────────────────────────────────
    [HttpPost("events")]
    public async Task<IActionResult> ReceiveEvent([FromBody] DeviceEventDto dto)
    {
        var t2         = DateTime.UtcNow;
        var reqTime    = DateTime.UtcNow;
        var sw         = Stopwatch.StartNew();
        var deviceId   = TokenService.GetDeviceId(User);
        var typeMid    = TokenService.GetTypeMid(User);
        var deviceType = TokenService.GetDeviceType(User);

        if (string.IsNullOrEmpty(typeMid))        return Unauthorized();
        if (dto is null)                          return BadRequest(new { error = "Empty event." });
        if (string.IsNullOrEmpty(dto.Message))    return BadRequest(new { error = "Message is required." });

        try
        {
            await _repo.InsertDeviceEventAsync(dto, deviceId, deviceType);

            sw.Stop();
            var t3 = DateTime.UtcNow;

            _actLog.LogBulkEvent(
                typeMid, deviceId, deviceType,
                count: 1,
                reqTime, sw.ElapsedMilliseconds,
                dto.T1, t2, t3,
                message: dto.Message,
                eventSeqNo: dto.EventSeqNo);   // ← pass message so log shows what was sent

            return Ok(new { Success = true, ServerSentAt = t3 , Seqno = dto.EventSeqNo });
        }
        catch (Exception ex)
        {
            _actLog.LogException("EVENT", typeMid, deviceId, ex);
            return StatusCode(500, new { error = "Event failed. See error log." });
        }
    }
}