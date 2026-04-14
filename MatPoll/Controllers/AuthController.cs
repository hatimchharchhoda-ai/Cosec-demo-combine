using MatPoll.DTOs;
using MatPoll.Repositories;
using MatPoll.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MatPoll.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly AppRepository _repo;
    private readonly TokenService  _tokenService;
    private readonly IConfiguration _config;

    public AuthController(
        AppRepository repo,
        TokenService  tokenService,
        IConfiguration config)
    {
        _repo         = repo;
        _tokenService = tokenService;
        _config       = config;
    }

    // ── POST /api/auth/login ──────────────────────────────────────────────────
    // Client sends: UserID + MACAddr + IPAddr
    // Server:
    //   1. Find user by UserID → check IsActive=1
    //   2. Find device by MACAddr + IPAddr → check IsActive=1
    //   3. Create JWT → set in cookie → return in body too
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] LoginRequest req)
    {
        // Validate device by DeviceID + MAC + IP
        var device = await _repo.FindDeviceAsync(req.DeviceID, req.MACAddr, req.IPAddr);

        if (device == null)
        {
            return Unauthorized(new LoginResponse
            {
                Success = false,
                Message = "Invalid DeviceID, MAC address, or IP address."
            });
        }

        if (device.IsActive != 1)
        {
            return Unauthorized(new LoginResponse
            {
                Success = false,
                Message = "Device is inactive. Contact administrator."
            });
        }

        // Create token with ONLY deviceId
        var token = _tokenService.CreateToken(device.DeviceID);
        var expMins = int.Parse(_config["Jwt:ExpiryMinutes"] ?? "1");

        TokenService.SetCookie(Response, token, expMins);

        return Ok(new LoginResponse
        {
            Success = true,
            Message = "Device authentication successful.",
            DeviceName = device.DeviceName,
            Token = token
        });
    }

    // ── POST /api/auth/refresh ────────────────────────────────────────────────
    // Client sends empty POST — server reads old token from cookie
    // Re-validates device are still active
    // Issues a fresh token with new expiry
    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<IActionResult> Refresh()
    {
        // Read token from cookie
        var oldToken = TokenService.ReadCookie(Request);
        if (string.IsNullOrEmpty(oldToken))
            return Unauthorized(new RefreshResponse
            {
                Success = false,
                Message = "No token found. Please login again."
            });

        // Validate the old token manually to read claims
      
        decimal deviceId;
        try
        {
            // Re-use the same validation logic
            var principal = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler()
                .ValidateToken(oldToken, new Microsoft.IdentityModel.Tokens.TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new Microsoft.IdentityModel.Tokens.SymmetricSecurityKey(
                        System.Text.Encoding.UTF8.GetBytes(_config["Jwt:Secret"]!)),
                    ValidateIssuer   = true,
                    ValidIssuer      = "MatPoll",
                    ValidateAudience = true,
                    ValidAudience    = "MatPollClient",
                    // Allow expired tokens on refresh (just check signature)
                    ValidateLifetime = false
                }, out _);

            deviceId = TokenService.GetDeviceId(principal);
        }
        catch
        {
            return Unauthorized(new RefreshResponse
            {
                Success = false,
                Message = "Invalid token. Please login again."
            });
        }

        // Re-check device are still active
        var device = await _repo.FindDeviceByIdAsync(deviceId);

        if (device == null || device.IsActive != 1)
            return Unauthorized(new RefreshResponse
            {
                Success = false,
                Message = "Device is no longer active."
            });

        // Issue new token
        var newToken = _tokenService.CreateToken(deviceId);
        var expMins  = int.Parse(_config["Jwt:ExpiryMinutes"] ?? "1");

        TokenService.SetCookie(Response, newToken, expMins);

        return Ok(new RefreshResponse
        {
            Success = true,
            Message = "Token refreshed.",
            Token   = newToken
        });
    }

    // ── POST /api/auth/logout 
    [HttpPost("logout")]
    [Authorize]
    public IActionResult Logout()
    {
        TokenService.ClearCookie(Response);
        return Ok(new { Success = true, Message = "Logged out." });
    }
}
