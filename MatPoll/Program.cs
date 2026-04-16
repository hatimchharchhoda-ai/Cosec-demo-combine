using System.Text;
using MatPoll.Data;
using MatPoll.Repositories;
using MatPoll.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Serilog;
using Serilog.Events; // ✅ REQUIRED

var builder = WebApplication.CreateBuilder(args);

// ✅ Read TestingLog BEFORE using it
var testingLog = builder.Configuration.GetValue<bool>("TestingLog");

// ── SERILOG CONFIG ───────────────────────────────────────
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()

    // Reduce noisy logs
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Database.Command", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)

    // Console
    .WriteTo.Console(outputTemplate: "{Message:lj}{NewLine}{Exception}")

    // INFO LOG (only Information)
    .WriteTo.Logger(lc => lc
        .Filter.ByIncludingOnly(e => e.Level == LogEventLevel.Information)
        .WriteTo.File("Logs/info-.log",
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: 30,
            outputTemplate: "{Message:lj}{NewLine}")
    )

    // DEBUG LOG (everything)
    .WriteTo.Logger(lc => lc
        .MinimumLevel.Debug()
        .WriteTo.File("Logs/debug-.log",
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: 30,
            outputTemplate: "{Message:lj}{NewLine}")
    )

    // ERROR LOG (only errors)
    .WriteTo.Logger(lc => lc
        .Filter.ByIncludingOnly(e => e.Level >= LogEventLevel.Error)
        .WriteTo.File("Logs/error-.log",
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: 30,
            outputTemplate: "{Message:lj}{NewLine}{Exception}")
    )

    // TESTING LOG (only if enabled)
    .WriteTo.Conditional(
        _ => testingLog,
        wt => wt.File("Logs/testing-.log",
            rollingInterval: RollingInterval.Day,
            retainedFileCountLimit: 30,
            outputTemplate: "{Message:lj}{NewLine}{Exception}")
    )

    .CreateLogger();

builder.Host.UseSerilog();

// ── DATABASE ─────────────────────────────────────────────
builder.Services.AddDbContext<AppDbContext>(opt =>
    opt.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// ── SERVICES ─────────────────────────────────────────────
builder.Services.AddScoped<AppRepository>();
builder.Services.AddSingleton<TokenService>();
builder.Services.AddSingleton<ActivityLogger>();
builder.Services.AddHostedService<StallRecoveryService>();

// ── JWT AUTH ─────────────────────────────────────────────
var secret = builder.Configuration["Jwt:Secret"]
    ?? throw new Exception("Jwt:Secret missing");

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opt =>
    {
        opt.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(secret)),
            ValidateIssuer = true,
            ValidIssuer = "MatPoll",
            ValidateAudience = true,
            ValidAudience = "MatPollClient",
            ClockSkew = TimeSpan.Zero
        };

        opt.Events = new JwtBearerEvents
        {
            OnMessageReceived = ctx =>
            {
                var cookieToken = ctx.Request.Cookies["mat_auth"];
                if (!string.IsNullOrEmpty(cookieToken))
                    ctx.Token = cookieToken;

                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddControllers();

// ── SWAGGER ──────────────────────────────────────────────
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "MatPoll API",
        Version = "v1",
        Description = "Device polling server — login by DeviceID+MAC+IP, TypeMID-based dispatch"
    });

    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

var app = builder.Build();

// ── PIPELINE ─────────────────────────────────────────────
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "MatPoll v1");
    c.RoutePrefix = string.Empty;
});

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

// ── START LOG ────────────────────────────────────────────
Log.Information("{Time} | SERVER STARTED | MatPoll is running",
    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

app.Run();