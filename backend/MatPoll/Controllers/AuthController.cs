using MatPoll.DTOs;
using MatPoll.Repositories;
using MatPoll.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Text;
using System.Security.Claims;

using Microsoft.IdentityModel.Tokens;
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
        var reqTime = DateTime.UtcNow;          
        var sw      = Stopwatch.StartNew();
        // var typeMid = TypeMidService.Generate(req.MACAddr, req.IPAddr);

        try
        {
              
            _actLog.LogTestingStep(
                "[LOGIN-START] {ReqTime}  DeviceType:{DeviceType}  MAC:{MAC}  IP:{IP}",
                reqTime.ToString("HH:mm:ss.fff"), req.DeviceType, req.MACAddr, req.IPAddr);

            var device = await _repo.FindDeviceAsync(req.DeviceType, req.MACAddr, req.IPAddr);

            if (device == null)
            {
                _actLog.LogLogin(
                    req.DeviceType, "?", 0,
                    false, "Device not found", sw.ElapsedMilliseconds,
                    req.MACAddr, req.IPAddr,
                    reqTime);                   
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
                    req.DeviceType, device.DeviceName ?? "?", device.DeviceType ?? 0,
                    false, "Device inactive", sw.ElapsedMilliseconds,
                    req.MACAddr, req.IPAddr,
                    reqTime);                   

                return Unauthorized(new LoginResponse
                {
                    Success      = false,
                    Message      = "Device is inactive.",
                    ServerSentAt = DateTime.UtcNow
                });
            }

            var expMins = int.Parse(_config["MatPollJwt:ExpiryMinutes"] ?? "60");
            var token   = _tokenService.CreateToken(
                device.DeviceID,
                device.DeviceType);

            TokenService.SetCookie(Response, token, expMins);

            _actLog.LogLogin(
                 device.DeviceID, device.DeviceName ?? "?",
                device.DeviceType ?? 0,
                true, "", sw.ElapsedMilliseconds,
                req.MACAddr, req.IPAddr,
                reqTime);                       

            return Ok(new LoginResponse
            {
                Success      = true,
                Message      = "Login successful.",
                DeviceId     = device.DeviceID,
                Token        = token,
               
                ServerSentAt = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _actLog.LogException("LOGIN",  req.DeviceType, ex);
            return StatusCode(500, new { error = "Login failed.", ServerSentAt = DateTime.UtcNow });
        }
    }

    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<IActionResult> Refresh()
    {
        var reqTime  = DateTime.UtcNow;         
        var sw       = Stopwatch.StartNew();
        var oldToken = TokenService.ReadCookie(Request);

        if (string.IsNullOrEmpty(oldToken))
            return Unauthorized(new RefreshResponse
                { Success = false, Message = "No token.", ServerSentAt = DateTime.UtcNow });

        decimal deviceId   = 0;
        // string  type   = string.Empty;
        decimal deviceType = 0;

        try
        {
            var part1  = _config["MatPollJwt:KeyPart1"]!;        // from appsettings
            var part2  = _config["MatPollJwt:KeyPart2"]!;        // from appsettings  
            var part3  = Environment.MachineName;    

            var combined = $"{part1}:{part2}:{part3}"; 

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(combined));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);    
            
            var principal = new JwtSecurityTokenHandler()
                .ValidateToken(oldToken,
                    new Microsoft.IdentityModel.Tokens.TokenValidationParameters
                    {
                        ValidateIssuerSigningKey = true,
                        IssuerSigningKey = key,
                        ValidateIssuer   = true, ValidIssuer   = "MatPoll",
                        ValidateAudience = true, ValidAudience = "MatPollClient",
                        ValidateLifetime = false
                    }, out _);

            deviceId   = TokenService.GetDeviceId(principal);
            // typeMid    = TokenService.GetTypeMid(principal);
            // deviceType = TokenService.GetDeviceType(principal);

            var device = await _repo.FindDeviceByIdAsync(deviceId);
            if (device == null || device.IsActive != 1)
            {
                _actLog.LogRefresh( deviceId, deviceType,
                    false, sw.ElapsedMilliseconds, reqTime); 
                return Unauthorized(new RefreshResponse
                    { Success = false, Message = "Device inactive.", ServerSentAt = DateTime.UtcNow });
            }

            await _repo.UpdateLastSeenAsync(deviceId);
            var expsec      = int.Parse(_config["MatPollJwt:ExpirySeconds"] ?? "60");
            var newToken     = _tokenService.CreateToken(
             
                device.DeviceID,
                device.DeviceType);

            TokenService.SetCookie(Response, newToken, expsec);

            _actLog.LogRefresh( deviceId, device.DeviceType ?? 0,
                true, sw.ElapsedMilliseconds, reqTime);      

            return Ok(new RefreshResponse
            {
                Success      = true,
                Message      = "Token refreshed.",
                Token        = newToken,
                
                ServerSentAt = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _actLog.LogException("REFRESH",  deviceId, ex);
            return StatusCode(500, new { error = "Refresh failed.", ServerSentAt = DateTime.UtcNow });
        }
    }

    [HttpPost("logout")]
    [Authorize(AuthenticationSchemes = "MatPollBearer")]
    public IActionResult Logout()
    {
        TokenService.ClearCookie(Response);
        return Ok(new { Success = true, Message = "Logged out.", ServerSentAt = DateTime.UtcNow });
    }
}
