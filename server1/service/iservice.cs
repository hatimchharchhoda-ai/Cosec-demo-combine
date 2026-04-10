using MatGenServer.DTOs;

namespace MatGenServer.Services.Interfaces;

public interface IAuthService
{
    Task<LoginResponseDto> LoginAsync(LoginRequestDto request);
    Task<RefreshTokenResponseDto> RefreshTokenAsync(HttpContext httpContext);
}

public interface IPollingService
{
    /// <summary>
    /// Called every 8 s by the client (GET /api/poll).
    /// Returns pending records OR instructs client to ACK first.
    /// </summary>
    Task<PollResponseDto> PollAsync(string deviceId, PollRequestDto request);

    /// <summary>Client confirms receipt of a batch.</summary>
    Task<AckResponseDto> AcknowledgeAsync(string deviceId, AckRequestDto request);
}

public interface ITokenService
{
    string GenerateAccessToken(string userId, int deviceId, string deviceType);
    (string userId, int deviceId) ValidateToken(string token);
}