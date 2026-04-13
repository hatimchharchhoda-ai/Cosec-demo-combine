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
        

        // 2. Validate device — must match both MAC and IP
        var device = await _repo.FindDeviceAsync(req.MACAddr, req.IPAddr);
        var d2 = await _repo.FindDeviceByIdAsync(req.DeviceID);

        if (device == null)
           {
            return Unauthorized(new LoginResponse
            {
                Success = false,
                Message = "Device not found. Check MAC address and IP address."
            });

            }

        if(d2 == null) 
        return Unauthorized(new LoginResponse
        {
            Success = false,
            Message = "Device Id Not Found "
        });

        if (device.IsActive != 1)
            return Unauthorized(new LoginResponse
            {
                Success = false,
                Message = "Device is inactive."
            });

        // 3. Create token
        var token   = _tokenService.CreateToken(device.DeviceID);

        var expMins = int.Parse(_config["Jwt:ExpiryMinutes"] ?? "60");

        // Set token in HttpOnly cookie
        TokenService.SetCookie(Response, token, expMins);

        return Ok(new LoginResponse
        {
            Success    = true,
            Message    = "Login successful.",
            DeviceName = device.DeviceName,
            Token      = token          // also in body for non-browser clients
        });
    }

    // ── POST /api/auth/refresh ────────────────────────────────────────────────
    // Client sends empty POST — server reads old token from cookie
    // Re-validates user + device are still active
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
        string userId;
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

            userId   = TokenService.GetUserId(principal);
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

        // Re-check user and device are still active
        var user   = await _repo.FindUserAsync(userId);
        var device = await _repo.FindDeviceByIdAsync(deviceId);

        if (user == null || user.IsActive != 1 || device == null || device.IsActive != 1)
            return Unauthorized(new RefreshResponse
            {
                Success = false,
                Message = "User or device is no longer active."
            });

        // Issue new token
        var newToken = _tokenService.CreateToken(deviceId);
        var expMins  = int.Parse(_config["Jwt:ExpiryMinutes"] ?? "60");

        TokenService.SetCookie(Response, newToken, expMins);

        return Ok(new RefreshResponse
        {
            Success = true,
            Message = "Token refreshed.",
            Token   = newToken
        });
    }

    // ── POST /api/auth/logout ─────────────────────────────────────────────────
    [HttpPost("logout")]
    [Authorize]
    public IActionResult Logout()
    {
        TokenService.ClearCookie(Response);
        return Ok(new { Success = true, Message = "Logged out." });
    }
}
