using MatPoll.DTOs;
using MatPoll.Repositories;
using MatPoll.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MatPoll.Controllers;

[ApiController]
[Route("api/poll")]
[Authorize]
public class PollController : ControllerBase
{
    private readonly AppRepository _repo;
    private readonly BatchCache    _cache;
    private readonly IConfiguration _config;

    public PollController(
        AppRepository  repo,
        BatchCache     cache,
        IConfiguration config)
    {
        _repo   = repo;
        _cache  = cache;
        _config = config;
    }

    [HttpGet]
    public async Task<IActionResult> Poll()
    {
        var deviceId = TokenService.GetDeviceId(User);
        if (deviceId == 0)
            return Unauthorized();

        // ── CASE 2: batch already in-flight ──────────────────────────────────
        var existingToken = _cache.Get(deviceId);
        if (existingToken != null)
        {
            // Confirm DB still has TrnStat=1 rows
            var inFlight = await _repo.GetDispatchedRowsAsync();
            if (inFlight.Count > 0)
            {
                // Tell device: you must ACK batch first
                return Ok(new PollResponse
                {
                    HasData      = false,
                    NeedAckFirst = true,
                    BatchToken   = existingToken,
                    TotalPending = await _repo.CountPendingAsync()
                });
            }

            // Rows are gone somehow — clean up stale cache entry
            _cache.Remove(deviceId);
        }

        // ── CASE 3: server restarted, cache lost, TrnStat=1 rows still in DB ─
        var orphaned = await _repo.GetDispatchedRowsAsync();
        if (orphaned.Count > 0)
        {
            var recoveredToken = Guid.NewGuid().ToString("N");
            _cache.Set(deviceId, recoveredToken);

            return Ok(new PollResponse
            {
                HasData      = true,
                BatchToken   = recoveredToken,
                TotalPending = await _repo.CountPendingAsync(),
                Rows = orphaned.Select(r => new TrnRow
                {
                    TrnID    = r.TrnID,
                    MsgStr   = r.MsgStr,
                    RetryCnt = r.RetryCnt ?? 0
                }).ToList()
            });
        }

        // ── CASE 1: normal — fetch next bunch ─────────────────────────────────
        var bunchSize = int.Parse(_config["PollingSettings:BunchSize"] ?? "3");
        var rows = await _repo.FetchAndMarkDispatchedAsync(bunchSize);

        if (rows.Count == 0)
        {
            // Nothing to send right now
            return Ok(new PollResponse
            {
                HasData      = false,
                TotalPending = 0
            });
        }

        var batchToken = Guid.NewGuid().ToString("N");
        _cache.Set(deviceId, batchToken);

        return Ok(new PollResponse
        {
            HasData      = true,
            BatchToken   = batchToken,
            TotalPending = await _repo.CountPendingAsync(),
            Rows = rows.Select(r => new TrnRow
            {
                TrnID    = r.TrnID,
                MsgStr   = r.MsgStr,
                RetryCnt = r.RetryCnt ?? 0
            }).ToList()
        });
    }

    [HttpPost("ack")]
    public async Task<IActionResult> Ack([FromBody] AckRequest req)
    {
        var deviceId = TokenService.GetDeviceId(User);
        if (deviceId == 0)
            return Unauthorized();

        // Check batch token matches what we issued
        var expectedToken = _cache.Get(deviceId);

        if (expectedToken == null)
        {
            // No active batch — maybe already ACKed (duplicate request)
            return Ok(new AckResponse
            {
                Success      = true,
                Message      = "Already acknowledged (no active batch found).",
                UpdatedCount = 0
            });
        }

        if (expectedToken != req.BatchToken)
        {
            return BadRequest(new AckResponse
            {
                Success = false,
                Message = $"Wrong batch token. Expected a different token."
            });
        }

        // Mark TrnStat=2 in DB
        var count = await _repo.MarkAcknowledgedAsync(req.TrnIDs);

        // Clear cache — device is now free to get next bunch
        _cache.Remove(deviceId);

        return Ok(new AckResponse
        {
            Success      = true,
            Message      = $"{count} rows acknowledged. Poll again for next batch.",
            UpdatedCount = count
        });
    }
}