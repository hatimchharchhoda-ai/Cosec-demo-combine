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

    private readonly MetricsService _metrics;

    public PollController(AppRepository repo, ActivityLogger actLog, IConfiguration config, MetricsService metrics)
    {
        _repo   = repo;
        _actLog = actLog;
        _config = config;
        _metrics = metrics;
    }

    // ── GET /api/poll ─────────────────────────────────────────────────────────
    [HttpGet]
    public async Task<IActionResult> Poll()
    {
        var reqTime    = DateTime.UtcNow;
        var sw         = Stopwatch.StartNew();
        var deviceId   = TokenService.GetDeviceId(User);
       // still used for JWT auth only
        var deviceType = TokenService.GetDeviceType(User);
      
        // if (!deviceId)
        //     return Unauthorized();

        try
        {
              await _repo.UpdateLastSeenAsync(deviceId);
              
            _actLog.LogTestingStep("[POLL-START] {ReqTime}  DeviceID:{DeviceID}  Type:{DeviceType}",
                reqTime.ToString("HH:mm:ss.fff"), deviceId, deviceType);
              
          
            // ── use DeviceID + DeviceType, not TypeMID ────────────────────
            var hasDispatched = await _repo.HasDispatchedRowsAsync(deviceId, deviceType);
            if (hasDispatched)
            {
                _actLog.LogPollNeedAck( deviceId, deviceType, reqTime, sw.ElapsedMilliseconds);
                return Ok(new PollResponse
                {
                    HasData      = false,
                    NeedAckFirst = true,
                    
                    Rows         = new List<TrnRow>(),
                    ServerSentAt = DateTime.UtcNow
                });
            }

            var bunchSize = int.Parse(_config["PollingSettings:BunchSize"] ?? "1");

            _actLog.LogTestingStep("[POLL-FETCH] DeviceID:{DeviceID}  Type:{DeviceType}  BunchSize:{Size}",
                deviceId, deviceType, bunchSize);

            // ── use DeviceID + DeviceType, not TypeMID ────────────────────
            var rows = await _repo.FetchAndMarkDispatchedAsync(deviceId, deviceType, bunchSize);

            if (rows.Count == 0)
            {
                _actLog.LogPollNoData( deviceId, deviceType, reqTime, sw.ElapsedMilliseconds);
                return Ok(new PollResponse
                {
                    HasData      = false,
                    NeedAckFirst = false,
                   
                    Rows         = new List<TrnRow>(),
                    ServerSentAt = DateTime.UtcNow
                });
            }

            sw.Stop();
             
            _metrics.RecordPoll(sw.ElapsedMilliseconds);
             
            _actLog.LogPollDataSent(
              deviceId, deviceType,
                rows,
                reqTime, sw.ElapsedMilliseconds);

            return Ok(new PollResponse
            {
                HasData      = true,
                NeedAckFirst = false,
               
                Rows = rows.Select(r => new TrnRow
                {
                    TrnID    = r.TrnID,
                    MsgStr   = r.MsgStr,
                    RetryCnt = r.RetryCnt ?? 0,
                    
                }).ToList(),
                ServerSentAt = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _metrics.RecordError();
            _actLog.LogException("POLL", deviceId, ex);
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
         // JWT auth only
        var deviceType = TokenService.GetDeviceType(User);

        // if (string.IsNullOrEmpty(typeMid))
        //     return Unauthorized();

        // if (req.TrnIDs == null || req.TrnIDs.Count == 0)
        //     return BadRequest(new { error = "TrnIDs list is empty." });
 
        if (req.TrnStatus == null || req.TrnStatus.Count == 0)
            return BadRequest(new { error = "TrnStatus map is empty." });

        try
        {
             await _repo.UpdateLastSeenAsync(deviceId);
            _actLog.LogTestingStep(
                "[ACK-START] {ReqTime}  DeviceID:{DeviceID}  Type:{DeviceType}  Count:{Count}",
                t2.ToString("HH:mm:ss.fff"), deviceId, deviceType, req.TrnStatus.Count);

            var ackWarnSecs = _config.GetValue<int>(
                "PollingSettings:AckTimeoutWarningSeconds", 30);

            // ── use DeviceID + DeviceType, not TypeMID ────────────────────
            var result = await _repo.MarkAcknowledgedAsync(
                req.TrnStatus, deviceId, deviceType);

            sw.Stop();
                _metrics.RecordAck(sw.ElapsedMilliseconds);
            long serverMs = sw.ElapsedMilliseconds;

            double upstreamMs = req.T1.HasValue
                ? Math.Round((t2 - req.T1.Value).TotalMilliseconds, 1) : -1;

            double fullRoundTripPrev = -1;
            if (req.T4Prev.HasValue && req.T1.HasValue && upstreamMs >= 0)
                fullRoundTripPrev = Math.Round(
                    (req.T1.Value - req.T4Prev.Value).TotalMilliseconds + upstreamMs, 1);

            _actLog.LogAck(
                deviceId, deviceType,
                req.TrnStatus, result,
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
            _metrics.RecordError();
             
            _actLog.LogException("ACK",  deviceId, ex);
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
           // JWT auth only
        var deviceType = TokenService.GetDeviceType(User);

        // if (string.IsNullOrEmpty(typeMid))
        //     return Unauthorized();

        try
        {
              await _repo.UpdateLastSeenAsync(deviceId);
            _actLog.LogTestingStep(
                "[RESTORE-START] {ReqTime}  DeviceID:{DeviceID}  Type:{DeviceType}",
                reqTime.ToString("HH:mm:ss.fff"), deviceId, deviceType);

            // ── use DeviceID + DeviceType, not TypeMID ────────────────────
            var count = await _repo.RestoreDispatchedAsync(deviceId, deviceType);

            _actLog.LogRestore( deviceId, deviceType,
                count, reqTime, sw.ElapsedMilliseconds);

            sw.Stop();
            _metrics.RecordEvent(sw.ElapsedMilliseconds);
            return Ok(new RestoreResponse
            {
                Success       = true,
                Message       = $"{count} rows restored to TrnStat=0.",
                RestoredCount = count,
              
                ServerSentAt  = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _metrics.RecordError();
            _actLog.LogException("RESTORE", deviceId, ex);
            return StatusCode(500, new { error = "Restore failed. See error log." });
        }
    }

    // ── POST /api/poll/events ─────────────────────────────────────────────────
    [HttpPost("events")]
    public async Task<IActionResult> ReceiveEvent([FromBody] DeviceEventDto dto)
    {
        var t2         = DateTime.UtcNow;
        var reqTime    = t2;
        var sw         = Stopwatch.StartNew();
        var deviceId   = TokenService.GetDeviceId(User);
        // var typeMid    = TokenService.GetTypeMid(User);    // JWT auth only
        var deviceType = TokenService.GetDeviceType(User);

        // if (deviceId)        return Unauthorized();
        if (dto is null)                          return BadRequest(new { error = "Empty event." });
        if (string.IsNullOrEmpty(dto.Message))    return BadRequest(new { error = "Message is required." });

        try
        {
             await _repo.UpdateLastSeenAsync(deviceId);
            await _repo.InsertDeviceEventAsync(dto, deviceId, deviceType);

            sw.Stop();
                _metrics.RecordEvent(sw.ElapsedMilliseconds);
            var t3 = DateTime.UtcNow;

            _actLog.LogBulkEvent(
                deviceId, deviceType,
                count:      1,
                reqTime:    reqTime,
                serverMs:   sw.ElapsedMilliseconds,
                t1:         dto.T1,
                t2:         t2,
                t3:         t3,
                message:    dto.Message,
                eventSeqNo: dto.EventSeqNo);

            return Ok(new { Success = true, ServerSentAt = t3, SeqNo = dto.EventSeqNo });
        }
        catch (Exception ex)
        {
            _metrics.RecordError();
            _actLog.LogException("EVENT", deviceId, ex);
            return StatusCode(500, new { error = "Event failed. See error log." });
        }
    }
}