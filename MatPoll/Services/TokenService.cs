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

    // Token now stores: deviceId + typeMid
   // CURRENT — missing parameters in signature

// FIX — add deviceName and deviceType as parameters
public string CreateToken(decimal deviceId, string typeMid, 
    string deviceName, decimal? deviceType)
{
    var secret  = _config["Jwt:Secret"]!;
    var expMins = int.Parse(_config["Jwt:ExpiryMinutes"] ?? "60");

    var key   = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
    var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

    var claims = new[]
    {
        new Claim("deviceId",   deviceId.ToString()),
        new Claim("typeMid",    typeMid),
        new Claim("deviceName", deviceName),
        new Claim("deviceType", deviceType?.ToString() ?? "0")
    };

    var token = new JwtSecurityToken(
        issuer:             "MatPoll",
        audience:           "MatPollClient",
        claims:             claims,
        expires:            DateTime.UtcNow.AddMinutes(expMins),
        signingCredentials: creds);

    return new JwtSecurityTokenHandler().WriteToken(token);
}

// Also add GetDeviceName helper
public static string GetDeviceName(ClaimsPrincipal user)
    => user.FindFirstValue("deviceName") ?? "?";
    public static decimal GetDeviceId(ClaimsPrincipal user)
    {
        var val = user.FindFirstValue("deviceId");
        return decimal.TryParse(val, out var d) ? d : 0;
    }

    public static decimal GetDeviceType(ClaimsPrincipal user)
{
    var val = user.FindFirstValue("deviceType");
    return decimal.TryParse(val, out var d) ? d : 0;
}

    // Read TypeMID directly from token — no DB call needed
    public static string GetTypeMid(ClaimsPrincipal user)
        => user.FindFirstValue("typeMid") ?? string.Empty;

    public static void SetCookie(HttpResponse response, string token, int expiryMinutes)
    {
        response.Cookies.Append("mat_auth", token, new CookieOptions
        {
            HttpOnly = true,
            Secure   = false,
            SameSite = SameSiteMode.Strict,
            Expires  = DateTimeOffset.UtcNow.AddMinutes(expiryMinutes)
        });
    }

    public static void ClearCookie(HttpResponse response)
        => response.Cookies.Delete("mat_auth");

    public static string? ReadCookie(HttpRequest request)
        => request.Cookies["mat_auth"];
}
