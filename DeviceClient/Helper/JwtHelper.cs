using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Collections.Generic;

public static class JwtHelper
{
    public static DateTime GetExpiry(string token)
    {
        var jwt    = new JwtSecurityTokenHandler().ReadJwtToken(token);
        var exp    = jwt.Claims.First(x => x.Type == "exp").Value;
        var expUnix = long.Parse(exp);
        return DateTimeOffset.FromUnixTimeSeconds(expUnix).UtcDateTime;
    }

    public static Dictionary<string, string> GetClaims(string token)
    {
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var claim in jwt.Claims)
        {
            // If multiple claims share the same type, keep the first one.
            if (!dict.ContainsKey(claim.Type))
                dict[claim.Type] = claim.Value;
        }
        return dict;
    }
}