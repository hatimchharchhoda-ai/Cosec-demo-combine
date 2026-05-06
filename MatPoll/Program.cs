using System.Text;
using MatPoll.Data;
using MatPoll.Repositories;
using MatPoll.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Serilog;
using Serilog.Events;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
// ─────────────────────────────────────────────────────────────────────────────
// 4 Log Files:
//   Logs/info-YYYYMMDD.log    → Login, Poll, ACK, Restore, Refresh (summary)
//   Logs/debug-YYYYMMDD.log   → Everything in info + row details, timings
//   Logs/error-YYYYMMDD.log   → Exceptions, DB failures, ACK mismatches, stalls
//   Logs/testing-YYYYMMDD.log → All internal steps (only when TestingLog=true)
// ─────────────────────────────────────────────────────────────────────────────

const string outputTemplate =
    "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {Level:u3}] {Message:lj}{NewLine}{Exception}";

const string consoleTemplate =
    "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}";

Log.Logger = new LoggerConfiguration()

    .MinimumLevel.Debug()

    .MinimumLevel.Override("Microsoft",                     LogEventLevel.Fatal)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Fatal)
    .MinimumLevel.Override("Microsoft.AspNetCore",          LogEventLevel.Fatal)
    .MinimumLevel.Override("Microsoft.Hosting",             LogEventLevel.Fatal)
    .MinimumLevel.Override("System",                        LogEventLevel.Fatal)

    .WriteTo.Logger(lc => lc
        .Filter.ByIncludingOnly(e =>
            e.Level == LogEventLevel.Debug &&
            HasSink(e, "debug", "testing"))
        .WriteTo.Console(outputTemplate: consoleTemplate))

    .WriteTo.Logger(lc => lc
        .Filter.ByIncludingOnly(e =>
            e.Level >= LogEventLevel.Information &&
            e.Level <= LogEventLevel.Warning &&
            HasSink(e, "info", "debug"))
        .WriteTo.File(
            path:                   "Logs/info-.log",
            rollingInterval:        RollingInterval.Day,
            retainedFileCountLimit: 30,
            outputTemplate:         outputTemplate))

    .WriteTo.Logger(lc => lc
        .Filter.ByIncludingOnly(e =>
            e.Level >= LogEventLevel.Debug &&
            e.Level <= LogEventLevel.Warning &&
            HasSink(e, "debug"))
        .WriteTo.File(
            path:                   "Logs/debug-.log",
            rollingInterval:        RollingInterval.Day,
            retainedFileCountLimit: 30,
            outputTemplate:         outputTemplate))

    .WriteTo.Logger(lc => lc
        .Filter.ByIncludingOnly(e =>
            e.Level >= LogEventLevel.Error &&
            HasSink(e, "error"))
        .WriteTo.File(
            path:                   "Logs/error-.log",
            rollingInterval:        RollingInterval.Day,
            retainedFileCountLimit: 30,
            outputTemplate:         outputTemplate))

    .WriteTo.Logger(lc => lc
        .Filter.ByIncludingOnly(e => HasSink(e, "testing"))
        .WriteTo.File(
            path:                   "Logs/testing-.log",
            rollingInterval:        RollingInterval.Day,
            retainedFileCountLimit: 7,
            outputTemplate:         outputTemplate))

    .WriteTo.Logger(lc => lc
        .Filter.ByIncludingOnly(e =>
            e.Level >= LogEventLevel.Information &&
            !e.Properties.ContainsKey("Sink"))
        .MinimumLevel.Override("Microsoft", LogEventLevel.Fatal)
        .MinimumLevel.Override("System",    LogEventLevel.Fatal)
        .WriteTo.File(
            path:                   "Logs/info-.log",
            rollingInterval:        RollingInterval.Day,
            retainedFileCountLimit: 30,
            outputTemplate:         outputTemplate))

    .CreateLogger();

static bool HasSink(LogEvent e, params string[] sinks)
{
    if (!e.Properties.TryGetValue("Sink", out var v)) return false;
    var val = v.ToString().Trim('"');
    return sinks.Contains(val);
}

// ─────────────────────────────────────────────────────────────────────────────

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog();

// ── Database ──────────────────────────────────────────────────────────────────
builder.Services.AddDbContext<AppDbContext>(opt =>
    opt.UseSqlServer(builder.Configuration
        .GetConnectionString("DefaultConnection")));

// ── Services ──────────────────────────────────────────────────────────────────
builder.Services.AddScoped<AppRepository>();
builder.Services.AddSingleton<TokenService>();
builder.Services.AddSingleton<ActivityLogger>();
builder.Services.AddHostedService<StallRecoveryService>();
builder.Services.AddHostedService<DeviceStatusService>();
builder.Services.AddHostedService<CommTrnRefillService>();
builder.Services.AddSingleton<MetricsService>();
builder.Services.AddHostedService(p => p.GetRequiredService<MetricsService>());
// Add after other services — ORDER MATTERS
// SyncService must be registered BEFORE StreamService
// because StreamService depends on SyncService
builder.Services.AddSingleton<GenetecSyncService>();
builder.Services.AddHostedService(p =>
    p.GetRequiredService<GenetecSyncService>());
// builder.Services.AddHostedService<GenetecStreamService>();
// ─────────────────────────────────────────────────────────────────────────────
//   — Response Compression
// ─────────────────────────────────────────────────────────────────────────────
// WHY: Compresses JSON responses before sending to devices.
//      Reduces payload size by 60-80%.
//      Example: 100KB poll response → becomes ~20KB → devices get data faster.
//      Zero cost to client — HttpClient/browser decompresses automatically.
// ─────────────────────────────────────────────────────────────────────────────
builder.Services.AddResponseCompression(opt =>
{
    opt.EnableForHttps = true; 
    
});

// ─────────────────────────────────────────────────────────────────────────────
//  — Health Check
// ─────────────────────────────────────────────────────────────────────────────
// WHY: Exposes GET /health endpoint.
//      Returns "Healthy" or "Unhealthy" + checks DB connection is alive.
//      Used by IIS, load balancers, or monitoring tools (UptimeRobot etc.)
//      to know if your server is actually working — not just running.
// ─────────────────────────────────────────────────────────────────────────────
builder.Services.AddHealthChecks()
    .AddSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection")!,
        name:    "database",
        failureStatus: HealthStatus.Unhealthy);

// ─────────────────────────────────────────────────────────────────────────────
//   — Rate Limiting
// ─────────────────────────────────────────────────────────────────────────────
// WHY: Prevents any single device/IP from flooding your server.
//      "poll" policy = max 100 requests per 60 seconds per device.
//      If exceeded → server returns 429 Too Many Requests automatically.
//      Protects DB from being hammered by a buggy or malicious device.
// ─────────────────────────────────────────────────────────────────────────────
// ── Rate Limiting ─────────────────────────────────────────────────────────────
builder.Services.AddRateLimiter(opt =>
{
    opt.AddPolicy("poll", ctx =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit         = 100,
                Window              = TimeSpan.FromSeconds(60),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit          = 0
            }));

    opt.OnRejected = async (ctx, ct) =>
    {
        var logger = ctx.HttpContext.RequestServices
            .GetRequiredService<ActivityLogger>();

        var ip   = ctx.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "";
        var path = ctx.HttpContext.Request.Path;

        logger.LogRateLimitExceeded(path, ip);

        ctx.HttpContext.Response.StatusCode = 429;
        await ctx.HttpContext.Response.WriteAsync(
            "{\"error\":\"Too many requests. Please slow down.\"}", ct);
    };
});

//— Request Timeout

builder.Services.AddRequestTimeouts(opt =>
{
    opt.DefaultPolicy = new Microsoft.AspNetCore.Http.Timeouts
        .RequestTimeoutPolicy
    {
        Timeout = TimeSpan.FromSeconds(30)  
    };
});

// ── JWT ───────────────────────────────────────────────────────────────────────
var part1 = builder.Configuration["Jwt:KeyPart1"]
    ?? throw new Exception("Jwt:KeyPart1 missing");
var part2 = builder.Configuration["Jwt:KeyPart2"]
    ?? throw new Exception("Jwt:KeyPart2 missing");
var part3 = Environment.MachineName;
var combined   = $"{part1}:{part2}:{part3}";
var signingKey = new SymmetricSecurityKey(
    Encoding.UTF8.GetBytes(combined));

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opt =>
    {
        opt.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey         = signingKey,
            ValidateIssuer   = true, ValidIssuer   = "MatPoll",
            ValidateAudience = true, ValidAudience = "MatPollClient",
            ClockSkew        = TimeSpan.Zero
        };
        opt.Events = new JwtBearerEvents
        {
            OnMessageReceived = ctx =>
            {
                var c = ctx.Request.Cookies["mat_auth"];
                if (!string.IsNullOrEmpty(c)) ctx.Token = c;
                return Task.CompletedTask;
            },

            OnAuthenticationFailed = ctx =>
            {
                // Skip login/auth endpoints — they don't need a token
                if (ctx.HttpContext.Request.Path
                        .StartsWithSegments("/api/auth"))
                    return Task.CompletedTask;

                var logger = ctx.HttpContext.RequestServices
                    .GetRequiredService<ActivityLogger>();
                var ip   = ctx.HttpContext.Connection
                    .RemoteIpAddress?.ToString() ?? "";
                var path = ctx.HttpContext.Request.Path;

                if (ctx.Exception is SecurityTokenExpiredException expEx)
                    logger.LogTokenExpired(path, ip, expEx.Expires);
                else
                    logger.LogTokenInvalid(path, ip, ctx.Exception.Message);

                return Task.CompletedTask;
            },

            OnChallenge = ctx =>
            {
                if (ctx.HttpContext.Request.Path
                        .StartsWithSegments("/api/auth"))
                    return Task.CompletedTask;

                if (ctx.AuthenticateFailure == null)
                {
                    var logger = ctx.HttpContext.RequestServices
                        .GetRequiredService<ActivityLogger>();
                    var ip   = ctx.HttpContext.Connection
                        .RemoteIpAddress?.ToString() ?? "";
                    var path = ctx.HttpContext.Request.Path;

                    logger.LogTokenMissing(path, ip);
                }
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddControllers();


//  — Swagger only in Development


if (builder.Environment.IsDevelopment())
{
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(c =>
    {
        c.SwaggerDoc("v1", new OpenApiInfo
        {
            Title       = "MatPoll API",
            Version     = "v1",
            Description = "Device polling — TypeMID dispatch, 4-file structured logging"
        });
        c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
        {
            Name         = "Authorization",
            Type         = SecuritySchemeType.Http,
            Scheme       = "bearer",
            BearerFormat = "JWT",
            In           = ParameterLocation.Header
        });
        c.AddSecurityRequirement(new OpenApiSecurityRequirement
        {{
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                    { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }});
    });
}


// var lifetime = app.Services
//     .GetRequiredService<IHostApplicationLifetime>();

// lifetime.ApplicationStopping.Register(() =>
// {
//     Log.Warning("[SHUTDOWN] Server stopping — draining requests...");
//     // give in-flight requests 10s to complete
//     Thread.Sleep(10000);
//     Log.Warning("[SHUTDOWN] Drain complete — shutting down");
//     Log.CloseAndFlush();
// });

// ── Build app ─────────────────────────────────────────────────────────────────
var app = builder.Build();

// ── DB Warmup ─────────────────────────────────────────────────────────────────
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.ExecuteSqlRawAsync("SELECT 1");
}


// — Global Exception Handler

app.UseExceptionHandler(errApp =>
{
    errApp.Run(async ctx =>
    {
        var logger = ctx.RequestServices
            .GetRequiredService<ActivityLogger>();

        var ex = ctx.Features
            .Get<IExceptionHandlerFeature>()?.Error;

        if (ex != null)
            logger.LogException("UNHANDLED_CRASH", 0, ex);

        ctx.Response.StatusCode  = 500;
        ctx.Response.ContentType = "application/json";
        await ctx.Response.WriteAsync(
            "{\"error\":\"Internal server error\"}");
    });
});


//   — Security Headers

app.Use(async (ctx, next) =>
{
    ctx.Response.Headers.Append("X-Content-Type-Options", "nosniff");
    ctx.Response.Headers.Append("X-Frame-Options",        "DENY");
    ctx.Response.Headers.Append("X-XSS-Protection",       "1; mode=block");
    ctx.Response.Headers.Append("Referrer-Policy",        "no-referrer");
    await next();
}); 




app.UseResponseCompression();

// ── Swagger (Development only) ────────────────────────────────────────────────
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c =>
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "MatPoll v1");
        c.RoutePrefix = string.Empty;
    });
}

app.UseRateLimiter();
app.UseRequestTimeouts();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();


// PRODUCTION ADD 9 — Health Check endpoint

app.MapHealthChecks("/health");

// ── Startup log ───────────────────────────────────────────────────────────────
Log.Information("MatPoll server started — Environment={Env}  TestingLog={Testing}",
    builder.Environment.EnvironmentName,
    builder.Configuration.GetValue<bool>("TestingLog", false));

app.Run();