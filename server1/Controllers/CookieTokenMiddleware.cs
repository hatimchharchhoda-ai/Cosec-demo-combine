namespace MatGenServer.Middleware;

/// <summary>
/// If no Authorization header is present but the "mat_token" cookie exists,
/// copies the cookie value into the Authorization header so the JWT middleware
/// can validate it normally. This supports both browser (cookie) and
/// native client (Bearer header) flows transparently.
/// </summary>
public class CookieTokenMiddleware
{
    private readonly RequestDelegate _next;

    public CookieTokenMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Headers.ContainsKey("Authorization"))
        {
            var token = context.Request.Cookies["mat_token"];
            if (!string.IsNullOrEmpty(token))
                context.Request.Headers.Append("Authorization", $"Bearer {token}");
        }

        await _next(context);
    }
}