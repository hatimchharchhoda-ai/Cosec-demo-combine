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
        var deviceType = TokenService.GetDeviceType(User); 
        var deviceName = TokenService.GetDeviceName(User);

        if (string.IsNullOrEmpty(typeMid))
            return Unauthorized();

        try
        {
            _actLog.LogTestingStep("[POLL-START] TypeMID:{TypeMID} DeviceID:{DeviceID}", typeMid, deviceId);

            // Step 1: TrnStat=1 rows exist for this device?
            var hasDispatched = await _repo.HasDispatchedRowsAsync(typeMid);
            if (hasDispatched)
            {
              _actLog.LogPollNeedAck(typeMid, deviceId, deviceType, reqTime, sw.ElapsedMilliseconds);
                return Ok(new PollResponse
                {
                    HasData      = false,
                    NeedAckFirst = true,
                    TotalPending = await _repo.CountPendingAsync(),
                    TypeMID      = typeMid,
                    Rows         = new List<TrnRow>(),
                    ServerSentAt =DateTime.UtcNow
                });
            }

            // Step 2: Fetch fresh TrnStat=0 rows
            var bunchSize = int.Parse(_config["PollingSettings:BunchSize"] ?? "1");

            _actLog.LogTestingStep("[POLL-FETCH] TypeMID:{TypeMID} BunchSize:{Size}", typeMid, bunchSize);

         
            var rows = await _repo.FetchAndMarkDispatchedAsync(typeMid, bunchSize);

           // count pending rows
            // foreach (var row in rows)
            //   _tracker.Track(row.TrnID);
              var pending = await _repo.CountPendingAsync();

            if (rows.Count == 0)
            {
               _actLog.LogPollNoData(typeMid, deviceId, deviceType, pending, reqTime, sw.ElapsedMilliseconds);
                return Ok(new PollResponse
                {
                    HasData      = false,
                    NeedAckFirst = false,
                    TotalPending = pending,
                    TypeMID      = typeMid,
                    Rows         = new List<TrnRow>(),
                    ServerSentAt = DateTime.UtcNow
                });
            }

            // Need device name for debug log — load from DB (cached by EF)
            var device = await _repo.FindDeviceByIdAsync(deviceId);

          _actLog.LogPollDataSent(typeMid, deviceId, deviceName, deviceType, rows, pending, reqTime, sw.ElapsedMilliseconds);

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
                ServerSentAt = DateTime.UtcNow
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
    var t2       = DateTime.UtcNow;
    var sw       = Stopwatch.StartNew();
    var deviceId = TokenService.GetDeviceId(User);
    var typeMid  = TokenService.GetTypeMid(User);
    var deviceType = TokenService.GetDeviceType(User); 
    var deviceName = TokenService.GetDeviceName(User);
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

        // ── AckDelay from in-memory tracker ──────────────────────────────
        // foreach (var id in req.TrnIDs)
        // {
        //     var delay = _tracker.GetDelaySeconds(id);
        //     if (delay.HasValue)
        //         result.AckDelays[id] = delay.Value;
        // }

        sw.Stop();
        var t3      = DateTime.UtcNow;
        long serverMs = sw.ElapsedMilliseconds;

        // ── Timing calculations ───────────────────────────────────────────
        double upstreamMs = req.T1.HasValue
            ? Math.Round((t2 - req.T1.Value).TotalMilliseconds, 1)
            : -1;

        double fullRoundTripPrev = -1;
        if (req.T4Prev.HasValue && req.T1.HasValue && upstreamMs >= 0)
            fullRoundTripPrev = Math.Round(
                (req.T1.Value - req.T4Prev.Value).TotalMilliseconds + upstreamMs, 1);

        // ── Log everything ────────────────────────────────────────────────
     _actLog.LogAck(typeMid, deviceId, deviceType, req.TrnIDs, result, t2, serverMs, upstreamMs, -1, fullRoundTripPrev, ackWarnSecs);

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
        var reqTime  = DateTime.UtcNow;
        var sw       = Stopwatch.StartNew();
        var deviceId = TokenService.GetDeviceId(User);
        var typeMid  = TokenService.GetTypeMid(User);
        var deviceType = TokenService.GetDeviceType(User); 
         var deviceName = TokenService.GetDeviceName(User);
        if (string.IsNullOrEmpty(typeMid))
            return Unauthorized();

        try
        {
            _actLog.LogTestingStep(
                "[RESTORE-START] TypeMID:{TypeMID} DeviceID:{DeviceID}", typeMid, deviceId);

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

    // for reciving events from client 
   //[HttpPost("event")]
// public async Task<IActionResult> ReceiveEvent([FromBody] DeviceEventDto dto)
// {
//     var t2       = DateTime.UtcNow;
//     var sw       = Stopwatch.StartNew();
//     var deviceId = TokenService.GetDeviceId(User);
//     var typeMid  = TokenService.GetTypeMid(User);
//     var deviceType = TokenService.GetDeviceType(User); 
//     var deviceName = TokenService.GetDeviceName(User);
//     if (string.IsNullOrEmpty(typeMid))
//         return Unauthorized();

//     await _repo.InsertDeviceEvent(dto, deviceId);

//     sw.Stop();
//     var t3 = DateTime.UtcNow;

//  _actLog.LogTiming("EVENT", typeMid, deviceId, deviceType, dto.T1, t2, t3);

//     return Ok(new
//     {
//         Success      = true,
//         Message      = "Event stored.",
//         ServerSentAt = DateTime.UtcNow
//     });
// }

[HttpPost("events/bulk")]
public async Task<IActionResult> ReceiveEventsBulk([FromBody] List<DeviceEventDto> dtos)
{
    var t2       = DateTime.UtcNow;           // server receive time
    var reqTime  = DateTime.Now;
    var sw       = Stopwatch.StartNew();
    var deviceId = TokenService.GetDeviceId(User);
    var typeMid  = TokenService.GetTypeMid(User);

    if (string.IsNullOrEmpty(typeMid)) return Unauthorized();
    if (dtos == null || dtos.Count == 0) return BadRequest(new { error = "Empty list." });
    if (dtos.Count > 100) return BadRequest(new { error = "Max 100 events per bulk call." });

    try
    {
        _actLog.LogTestingStep(
            "[BULK-EVENT-START] TypeMID:{TypeMID} DeviceID:{DeviceID} Count:{Count}",
            typeMid, deviceId, dtos.Count);

        var device = await _repo.FindDeviceByIdAsync(deviceId);
        await _repo.InsertDeviceEventsBulkAsync(dtos, deviceId, device?.DeviceType);

        sw.Stop();
        var t3 = DateTime.UtcNow;             // server done time

        // T1 = earliest client send time from the batch
        var t1 = dtos
            .Where(d => d.T1.HasValue)
            .Select(d => d.T1)
            .OrderBy(d => d)
            .FirstOrDefault();

        _actLog.LogBulkEvent(
            typeMid, deviceId, device?.DeviceType,
            dtos.Count, reqTime,
            sw.ElapsedMilliseconds,
            t1, t2, t3);

        return Ok(new { Success = true, Stored = dtos.Count, ServerSentAt = DateTime.UtcNow });
    }
    catch (Exception ex)
    {
        _actLog.LogException("BULK-EVENT", typeMid, deviceId, ex);
        return StatusCode(500, new { error = "Bulk event failed. See error log." });
    }
}
}
