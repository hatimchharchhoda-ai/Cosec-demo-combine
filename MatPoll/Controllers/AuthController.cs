using MatPoll.DTOs;
using MatPoll.Repositories;
using MatPoll.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Text;

namespace MatPoll.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly AppRepository  _repo;
    private readonly TokenService   _tokenService;
    private readonly ActivityLogger _actLog;
    private readonly IConfiguration _config;

    public AuthController(AppRepository repo, TokenService tokenService,
        ActivityLogger actLog, IConfiguration config)
    {
        _repo         = repo;
        _tokenService = tokenService;
        _actLog       = actLog;
        _config       = config;
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] LoginRequest req)
    {
        var sw      = Stopwatch.StartNew();
        var typeMid = TypeMidService.Generate(req.MACAddr, req.IPAddr);

        try
        {
            _actLog.LogTestingStep(
                "[LOGIN-START] DeviceType:{DeviceType} MAC:{MAC} IP:{IP} ",
                req.DeviceType, req.MACAddr, req.IPAddr, typeMid);

            var device = await _repo.FindDeviceAsync(req.DeviceType, req.MACAddr, req.IPAddr);

            if (device == null)
            {
                _actLog.LogLogin(
                    typeMid, req.DeviceType, "?", 0,
                    false, "Device not found", sw.ElapsedMilliseconds,
                    req.MACAddr, req.IPAddr);   

                return Unauthorized(new LoginResponse
                {
                    Success      = false,
                    Message      = "Device not found. Check DeviceID, MAC and IP.",
                    ServerSentAt = DateTime.UtcNow
                });
            }

            if (device.IsActive != 1)
            {
                _actLog.LogLogin(
                    typeMid, req.DeviceType, device.DeviceName ?? "?",
                    device.DeviceType ?? 0,
                    false, "Device inactive", sw.ElapsedMilliseconds,
                    req.MACAddr, req.IPAddr);   // ← pass mac+ip

                return Unauthorized(new LoginResponse
                {
                    Success      = false,
                    Message      = "Device is inactive.",
                    ServerSentAt = DateTime.UtcNow
                });
            }

            var expMins = int.Parse(_config["Jwt:ExpiryMinutes"] ?? "60");
            var token   = _tokenService.CreateToken(
                device.DeviceID,
                typeMid,
                device.DeviceName ?? "?",
                device.DeviceType);

            TokenService.SetCookie(Response, token, expMins);

            _actLog.LogLogin(
                typeMid, device.DeviceID, device.DeviceName ?? "?",
                device.DeviceType ?? 0,
                true, "", sw.ElapsedMilliseconds,
                req.MACAddr, req.IPAddr);       // ← pass mac+ip

            return Ok(new LoginResponse
            {
                Success      = true,
                Message      = "Login successful.",
                DeviceId     = device.DeviceID,
                Token        = token,
                TypeMID      = typeMid,
                ServerSentAt = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _actLog.LogException("LOGIN", typeMid, req.DeviceType, ex);
            return StatusCode(500, new { error = "Login failed.", ServerSentAt = DateTime.UtcNow });
        }
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<IActionResult> Refresh()
    {
        var sw       = Stopwatch.StartNew();
        var oldToken = TokenService.ReadCookie(Request);

        if (string.IsNullOrEmpty(oldToken))
            return Unauthorized(new RefreshResponse
                { Success = false, Message = "No token.", ServerSentAt = DateTime.UtcNow });

        decimal deviceId   = 0;
        string  typeMid    = string.Empty;
        decimal deviceType = 0;

        try
        {
            var principal = new JwtSecurityTokenHandler()
                .ValidateToken(oldToken,
                    new Microsoft.IdentityModel.Tokens.TokenValidationParameters
                    {
                        ValidateIssuerSigningKey = true,
                        IssuerSigningKey = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(
                            Encoding.UTF8.GetBytes(_config["Jwt:Secret"]!)),
                        ValidateIssuer   = true, ValidIssuer   = "MatPoll",
                        ValidateAudience = true, ValidAudience = "MatPollClient",
                        ValidateLifetime = false
                    }, out _);

            deviceId   = TokenService.GetDeviceId(principal);
            typeMid    = TokenService.GetTypeMid(principal);
            deviceType = TokenService.GetDeviceType(principal);

            var device = await _repo.FindDeviceByIdAsync(deviceId);
            if (device == null || device.IsActive != 1)
            {
                _actLog.LogRefresh(typeMid, deviceId, deviceType, false, sw.ElapsedMilliseconds);
                return Unauthorized(new RefreshResponse
                    { Success = false, Message = "Device inactive.", ServerSentAt = DateTime.UtcNow });
            }

            var freshTypeMid = TypeMidService.Generate(device.MACAddr ?? "", device.IPAddr ?? "");
            var expMins      = int.Parse(_config["Jwt:ExpiryMinutes"] ?? "60");
            var newToken     = _tokenService.CreateToken(
                deviceId, freshTypeMid,
                device.DeviceName ?? "?",
                device.DeviceType);

            TokenService.SetCookie(Response, newToken, expMins);

            _actLog.LogRefresh(freshTypeMid, deviceId, device.DeviceType ?? 0, true, sw.ElapsedMilliseconds);

            return Ok(new RefreshResponse
            {
                Success      = true,
                Message      = "Token refreshed.",
                Token        = newToken,
                TypeMID      = freshTypeMid,
                ServerSentAt = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _actLog.LogException("REFRESH", typeMid, deviceId, ex);
            return StatusCode(500, new { error = "Refresh failed.", ServerSentAt = DateTime.UtcNow });
        }
    }

    [HttpPost("logout")]
    [Authorize]
    public IActionResult Logout()
    {
        TokenService.ClearCookie(Response);
        return Ok(new { Success = true, Message = "Logged out.", ServerSentAt = DateTime.UtcNow });
    }
}