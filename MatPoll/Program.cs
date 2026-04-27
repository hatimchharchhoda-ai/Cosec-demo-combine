using System.Text;
using MatPoll.Data;
using MatPoll.Repositories;
using MatPoll.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Serilog;
using Serilog.Events;
using Serilog.Filters;

// ─────────────────────────────────────────────────────────────────────────────
// 4 Log Files:
//
//   Logs/info-YYYYMMDD.log    → Login, Poll, ACK, Restore, Refresh (summary)
//   Logs/debug-YYYYMMDD.log   → Everything in info + row details, timings
//   Logs/error-YYYYMMDD.log   → Exceptions, DB failures, ACK mismatches, stalls
//   Logs/testing-YYYYMMDD.log → All internal steps (only when TestingLog=true)
//
// Console → shows only Debug level logs (no duplicates)
//
// Each sink filters by the "Sink" context property set in ActivityLogger.
// Framework noise (EF SQL, ASP.NET pipeline) is silenced globally.
// ─────────────────────────────────────────────────────────────────────────────

const string outputTemplate =
    "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {Level:u3}] {Message:lj}{NewLine}{Exception}";

const string consoleTemplate =
    "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}";

Log.Logger = new LoggerConfiguration()

    // ── Global minimum: accept Debug and above ───────────────────────────────
    .MinimumLevel.Debug()

    // ── Silence all framework namespaces ────────────────────────────────────
    .MinimumLevel.Override("Microsoft",                         LogEventLevel.Fatal)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore",     LogEventLevel.Fatal)
    .MinimumLevel.Override("Microsoft.AspNetCore",              LogEventLevel.Fatal)
    .MinimumLevel.Override("Microsoft.Hosting",                 LogEventLevel.Fatal)
    .MinimumLevel.Override("System",                            LogEventLevel.Fatal)

    // ── Console: show ONLY Debug level from your code ────────────────────────
    // No Info/Warn/Error on console → no duplicate lines
    // Only _debug.Debug(...) and _testing.Debug(...) appear here
    .WriteTo.Logger(lc => lc
        .Filter.ByIncludingOnly(e =>
            e.Level == LogEventLevel.Debug &&
            HasSink(e, "debug", "testing"))
        .WriteTo.Console(outputTemplate: consoleTemplate))

    // ── info.log: INFO + WARN only, Sink=info or Sink=debug ─────────────────
    // Summary logs: LOGIN, POLL, ACK, RESTORE, REFRESH, EVENT
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

    // ── debug.log: DEBUG + INFO + WARN, Sink=debug only ─────────────────────
    // Detailed logs: timings, ReqTime, T1/T2/T3
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

    // ── error.log: ERROR + FATAL, Sink=error only ───────────────────────────
    // Exceptions, DB failures, ACK mismatches, stalls
    .WriteTo.Logger(lc => lc
        .Filter.ByIncludingOnly(e =>
            e.Level >= LogEventLevel.Error &&
            HasSink(e, "error"))
        .WriteTo.File(
            path:                   "Logs/error-.log",
            rollingInterval:        RollingInterval.Day,
            retainedFileCountLimit: 30,
            outputTemplate:         outputTemplate))

    // ── testing.log: all levels, Sink=testing ───────────────────────────────
    // Internal steps — only written when TestingLog=true in appsettings.json
    .WriteTo.Logger(lc => lc
        .Filter.ByIncludingOnly(e => HasSink(e, "testing"))
        .WriteTo.File(
            path:                   "Logs/testing-.log",
            rollingInterval:        RollingInterval.Day,
            retainedFileCountLimit: 7,
            outputTemplate:         outputTemplate))

    // ── Startup/shutdown → info.log only (no Sink property on these) ─────────
    // Catches Log.Information("MatPoll server started...") and similar
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

// ── Helper: check if event has a specific Sink property value ─────────────────
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
    opt.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// ── Services ──────────────────────────────────────────────────────────────────
builder.Services.AddScoped<AppRepository>();
builder.Services.AddSingleton<TokenService>();
builder.Services.AddSingleton<ActivityLogger>();
builder.Services.AddHostedService<StallRecoveryService>();

// ── JWT ───────────────────────────────────────────────────────────────────────
// var secret = builder.Configuration["Jwt:Secret"]
//     ?? throw new Exception("Jwt:Secret missing");

var part1   = builder.Configuration["Jwt:KeyPart1"]
    ?? throw new Exception("Jwt:KeyPart1 missing");
var part2   = builder.Configuration["Jwt:KeyPart2"]
    ?? throw new Exception("Jwt:KeyPart2 missing");
var part3   = Environment.MachineName;

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
            }
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddControllers();

// ── Swagger ───────────────────────────────────────────────────────────────────
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

// ── Build app ─────────────────────────────────────────────────────────────────
var app = builder.Build();

// ── DB Warmup: force EF init + open first connection at startup ───────────────
// Prevents cold-start delay on first real request
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.ExecuteSqlRawAsync("SELECT 1");
}

// ── Middleware pipeline ───────────────────────────────────────────────────────
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "MatPoll v1");
    c.RoutePrefix = string.Empty;
});
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

// ── Startup log ───────────────────────────────────────────────────────────────
// Goes to info.log via the startup sink (no Sink property → caught by last WriteTo.Logger)
Log.Information("MatPoll server started — TestingLog={Testing}",
    builder.Configuration.GetValue<bool>("TestingLog", false));

app.Run();