using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace MatPoll.Services;

public class TokenService
{
    private readonly IConfiguration _config;

    public TokenService(IConfiguration config)
    {
        _config = config;
    }

    // Create a JWT that stores userId + deviceId inside
    public string CreateToken(decimal deviceId)
    {
        var secret = _config["Jwt:Secret"]!;
        var expMins = int.Parse(_config["Jwt:ExpiryMinutes"] ?? "1");

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
        new Claim("deviceId", deviceId.ToString())
    };

        var token = new JwtSecurityToken(
            issuer: "MatPoll",
            audience: "MatPollClient",
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(expMins),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    // Read userId from the validated token claims
    public static string GetUserId(ClaimsPrincipal user)
        => user.FindFirstValue("userId") ?? string.Empty;

    // Read deviceId from the validated token claims
    public static decimal GetDeviceId(ClaimsPrincipal user)
    {
        var val = user.FindFirstValue("deviceId");
        return decimal.TryParse(val, out var d) ? d : 0;
    }

    // Set token in HttpOnly cookie (browser-safe, JS cannot read it)
    public static void SetCookie(HttpResponse response, string token, int expiryMinutes)
    {
        response.Cookies.Append("mat_auth", token, new CookieOptions
        {
            HttpOnly  = true,                          // JS cannot access
            Secure    = true,                         // set true in production (HTTPS)
            SameSite  = SameSiteMode.None,
            Expires   = DateTimeOffset.UtcNow.AddMinutes(expiryMinutes)
        });
    }

    // Remove cookie on logout
    public static void ClearCookie(HttpResponse response)
    {
        response.Cookies.Delete("mat_auth");
    }

    // Read token from cookie (used by refresh endpoint)
    public static string? ReadCookie(HttpRequest request)
        => request.Cookies["mat_auth"];
}
