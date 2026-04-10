using MatGenServer.DTOs;
using MatGenServer.Repositories.Interfaces;
using MatGenServer.Services.Interfaces;

namespace MatGenServer.Services;

public class AuthService : IAuthService
{
    private readonly IDeviceRepository _deviceRepo;
    private readonly IUserRepository _userRepo;
    private readonly ITokenService _tokenService;
    private readonly IConfiguration _config;

    public AuthService(
        IDeviceRepository deviceRepo,
        IUserRepository userRepo,
        ITokenService tokenService,
        IConfiguration config)
    {
        _deviceRepo = deviceRepo;
        _userRepo = userRepo;
        _tokenService = tokenService;
        _config = config;
    }

    // ── Login ─────────────────────────────────────────────────────────────────

    public async Task<LoginResponseDto> LoginAsync(LoginRequestDto request)
    {
        // 1. Validate user
        var user = await _userRepo.GetByUserIDAsync(request.DeviceID);
        if (user is null)
            return Fail("User not found.");

        if (user.IsActive != 1)
            return Fail("User account is inactive.");

        // 2. Validate device (MAC + IP must match a registered, active device)
        var device = await _deviceRepo.GetByMACAndIPAsync(request.MACAddr, request.IPAddr);
        if (device is null)
            return Fail("Device not registered. MAC/IP combination not found.");

        if (!device.IsActive)
            return Fail("Device is inactive.");

        // 3. Generate JWT
        var token = _tokenService.GenerateAccessToken(user.UserID, device.DeviceID, device.DeviceType ?? "");

        // 4. Set HttpOnly cookie  (caller sets cookie on HttpContext)
        return new LoginResponseDto
        {
            Success = true,
            Message = "Login successful.",
            AccessToken = token,
            DeviceName = device.DeviceName,
            DeviceType = device.DeviceType
        };
    }

    // ── Refresh ───────────────────────────────────────────────────────────────

    public async Task<RefreshTokenResponseDto> RefreshTokenAsync(HttpContext httpContext)
    {
        var existing = httpContext.Request.Cookies["mat_token"];
        if (string.IsNullOrEmpty(existing))
            return new RefreshTokenResponseDto { Success = false, Message = "No token cookie found." };

        try
        {
            var (userId, deviceId) = _tokenService.ValidateToken(existing);

            var user = await _userRepo.GetByUserIDAsync(userId);
            var device = await _deviceRepo.GetByDeviceIDAsync(deviceId);

            if (user is null || user.IsActive != 1 || device is null || !device.IsActive)
                return new RefreshTokenResponseDto { Success = false, Message = "User or device no longer active." };

            var newToken = _tokenService.GenerateAccessToken(userId, deviceId, device.DeviceType ?? "");
            return new RefreshTokenResponseDto { Success = true, AccessToken = newToken };
        }
        catch
        {
            return new RefreshTokenResponseDto { Success = false, Message = "Invalid or expired token." };
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static LoginResponseDto Fail(string msg) =>
        new() { Success = false, Message = msg };
}