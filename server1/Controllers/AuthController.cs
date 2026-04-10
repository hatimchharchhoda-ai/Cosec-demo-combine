using MatGenServer.DTOs;
using MatGenServer.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MatGenServer.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly IAuthService _authService;
    private readonly IConfiguration _config;

    public AuthController(IAuthService authService, IConfiguration config)
    {
        _authService = authService;
        _config = config;
    }

    // POST /api/auth/login
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login([FromBody] LoginRequestDto request)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var result = await _authService.LoginAsync(request);

        if (!result.Success)
            return Unauthorized(result);

        // Set JWT in HttpOnly cookie
        var cookieOptions = new CookieOptions
        {
            HttpOnly = true,
            Secure = true,          // set false for local HTTP dev
            SameSite = SameSiteMode.Strict,
            Expires = DateTimeOffset.UtcNow.AddMinutes(
                            int.TryParse(_config["Jwt:ExpiryMinutes"], out var m) ? m : 60)
        };
        Response.Cookies.Append("mat_token", result.AccessToken!, cookieOptions);

        // Also return token in body so native clients (non-browser) can store it
        return Ok(result);
    }

    // POST /api/auth/refresh
    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<IActionResult> Refresh()
    {
        var result = await _authService.RefreshTokenAsync(HttpContext);

        if (!result.Success)
            return Unauthorized(result);

        // Rewrite cookie with new token
        var cookieOptions = new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Expires = DateTimeOffset.UtcNow.AddMinutes(
                           int.TryParse(_config["Jwt:ExpiryMinutes"], out var m) ? m : 60)
        };
        Response.Cookies.Append("mat_token", result.AccessToken!, cookieOptions);

        return Ok(result);
    }

    // POST /api/auth/logout
    [HttpPost("logout")]
    [Authorize]
    public IActionResult Logout()
    {
        Response.Cookies.Delete("mat_token");
        return Ok(new { Success = true, Message = "Logged out." });
    }
}