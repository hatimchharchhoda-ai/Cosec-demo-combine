using MatPoll.DTOs;
using MatPoll.Repositories;
using MatPoll.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;

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

    // POST /api/auth/login
    // Client sends: DeviceID + MACAddr + IPAddr
    // Server: checks device exists + active, generates TypeMID from MAC+IP,
    //         creates token with deviceId + typeMid inside
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] LoginRequest req)
    {
        var sw = Stopwatch.StartNew();
        // Generate TypeMID from MAC+IP right away (even before DB check)
        var typeMid = TypeMidService.Generate(req.MACAddr, req.IPAddr);

        var device = await _repo.FindDeviceAsync(req.DeviceID, req.MACAddr, req.IPAddr);

        if (device == null)
        {
            _actLog.LogLogin(typeMid, req.DeviceID, "?",
                false, "Device not found", sw.ElapsedMilliseconds);
            return Unauthorized(new LoginResponse
            {
                Success = false,
                Message = "Device not found. Check DeviceID, MAC and IP."
            });
        }

        if (device.IsActive != 1)
        {
            _actLog.LogLogin(typeMid, req.DeviceID, device.DeviceName ?? "?",
                false, "Device inactive", sw.ElapsedMilliseconds);
            return Unauthorized(new LoginResponse
            {
                Success = false,
                Message = "Device is inactive."
            });
        }

        var expMins = int.Parse(_config["Jwt:ExpiryMinutes"] ?? "1");
        var token   = _tokenService.CreateToken(device.DeviceID, typeMid);
        TokenService.SetCookie(Response, token, expMins);

        _actLog.LogLogin(typeMid, device.DeviceID, device.DeviceName ?? "?",
            true, "", sw.ElapsedMilliseconds);

        return Ok(new LoginResponse
        {
            Success    = true,
            Message    = "Login successful.",
            DeviceName = device.DeviceName,
            Token      = token,
            TypeMID    = typeMid
        });
    }

    // POST /api/auth/refresh
    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<IActionResult> Refresh()
    {
        var sw       = Stopwatch.StartNew();
        var oldToken = TokenService.ReadCookie(Request);

        if (string.IsNullOrEmpty(oldToken))
            return Unauthorized(new RefreshResponse
            {
                Success = false, Message = "No token. Please login."
            });

        decimal deviceId;
        string  typeMid;
        try
        {
            var principal = new System.IdentityModel.Tokens.Jwt
                .JwtSecurityTokenHandler()
                .ValidateToken(oldToken,
                    new Microsoft.IdentityModel.Tokens.TokenValidationParameters
                    {
                        ValidateIssuerSigningKey = true,
                        IssuerSigningKey = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(
                            System.Text.Encoding.UTF8.GetBytes(_config["Jwt:Secret"]!)),
                        ValidateIssuer   = true, ValidIssuer   = "MatPoll",
                        ValidateAudience = true, ValidAudience = "MatPollClient",
                        ValidateLifetime = false
                    }, out _);
            deviceId = TokenService.GetDeviceId(principal);
            typeMid  = TokenService.GetTypeMid(principal);
        }
        catch
        {
            return Unauthorized(new RefreshResponse
            {
                Success = false, Message = "Invalid token."
            });
        }

        var device = await _repo.FindDeviceByIdAsync(deviceId);
        if (device == null || device.IsActive != 1)
        {
            _actLog.LogRefresh(typeMid, deviceId, false, sw.ElapsedMilliseconds);
            return Unauthorized(new RefreshResponse
            {
                Success = false, Message = "Device inactive."
            });
        }

        // Regenerate TypeMID from device's actual MAC+IP (most up to date)
        var freshTypeMid = TypeMidService.Generate(
            device.MACAddr ?? "", device.IPAddr ?? "");

        var expMins  = int.Parse(_config["Jwt:ExpiryMinutes"] ?? "1");
        var newToken = _tokenService.CreateToken(deviceId, freshTypeMid);
        TokenService.SetCookie(Response, newToken, expMins);

        _actLog.LogRefresh(freshTypeMid, deviceId, true, sw.ElapsedMilliseconds);

        return Ok(new RefreshResponse
        {
            Success = true,
            Message = "Token refreshed.",
            Token   = newToken,
            TypeMID = freshTypeMid
        });
    }

    // POST /api/auth/logout
    [HttpPost("logout")]
    [Authorize]
    public IActionResult Logout()
    {
        TokenService.ClearCookie(Response);
        return Ok(new { Success = true, Message = "Logged out." });
    }
}
