using System.Security.Claims;
using MatGenServer.DTOs;
using MatGenServer.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MatGenServer.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class PollController : ControllerBase
{
    private readonly IPollingService _pollingService;
    private readonly ILogger<PollController> _logger;

    public PollController(IPollingService pollingService, ILogger<PollController> logger)
    {
        _pollingService = pollingService;
        _logger = logger;
    }

    // GET /api/poll
    // Called by client every 8 seconds.
    // Body: { "lastBatchToken": "..." }  (null on first poll or when no prior batch)
    [HttpGet]
    public async Task<IActionResult> Poll([FromBody] PollRequestDto? request)
    {
        var deviceId = GetDeviceId();
        if (deviceId == null)
            return Unauthorized(new { Message = "Device ID missing from token." });

        request ??= new PollRequestDto();
        var result = await _pollingService.PollAsync(deviceId, request);
        return Ok(result);
    }

    // POST /api/poll/ack
    // Client sends this after successfully processing a batch.
    [HttpPost("ack")]
    public async Task<IActionResult> Acknowledge([FromBody] AckRequestDto request)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var deviceId = GetDeviceId();
        if (deviceId == null)
            return Unauthorized(new { Message = "Device ID missing from token." });

        var result = await _pollingService.AcknowledgeAsync(deviceId, request);

        if (!result.Success)
            return BadRequest(result);

        return Ok(result);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private string? GetDeviceId() =>
        User.FindFirstValue("deviceId");
}