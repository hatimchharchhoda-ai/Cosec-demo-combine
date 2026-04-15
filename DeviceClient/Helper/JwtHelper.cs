using System.IdentityModel.Tokens.Jwt;
using System.Linq;

public static class JwtHelper
{
    public static DateTime GetExpiry(string token)
    {
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        var exp = jwt.Claims.First(x => x.Type == "exp").Value;
        var expUnix = long.Parse(exp);
        return DateTimeOffset.FromUnixTimeSeconds(expUnix).UtcDateTime;
    }
}