using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using MatGenServer.Services.Interfaces;
using Microsoft.IdentityModel.Tokens;

namespace MatGenServer.Services;

public class TokenService : ITokenService
{
    private readonly IConfiguration _config;
    private readonly string _secret;
    private readonly string _issuer;
    private readonly string _audience;
    private readonly int _expiryMinutes;

    public TokenService(IConfiguration config)
    {
        _config = config;
        _secret = config["Jwt:Secret"] ?? throw new InvalidOperationException("Jwt:Secret missing");
        _issuer = config["Jwt:Issuer"] ?? "MatGenServer";
        _audience = config["Jwt:Audience"] ?? "MatGenClient";
        _expiryMinutes = int.TryParse(config["Jwt:ExpiryMinutes"], out var m) ? m : 60;
    }

    public string GenerateAccessToken(string userId, int deviceId, string deviceType)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_secret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId),
            new Claim("deviceId",   deviceId.ToString()),
            new Claim("deviceType", deviceType ?? ""),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var token = new JwtSecurityToken(
            issuer: _issuer,
            audience: _audience,
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(_expiryMinutes),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public (string userId, int deviceId) ValidateToken(string token)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_secret));
        var handler = new JwtSecurityTokenHandler();

        var principal = handler.ValidateToken(token, new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = key,
            ValidateIssuer = true,
            ValidIssuer = _issuer,
            ValidateAudience = true,
            ValidAudience = _audience,
            ClockSkew = TimeSpan.Zero
        }, out _);

        var userId = principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? "";
        var deviceId = int.Parse(principal.FindFirstValue("deviceId") ?? "0");
        return (userId, deviceId);
    }
}