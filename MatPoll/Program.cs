using System.Text;
using MatPoll.Data;
using MatPoll.Repositories;
using MatPoll.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()

    // ❌ ONLY disable SQL logs (THIS is the key line)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Database.Command", Serilog.Events.LogEventLevel.Warning)

    .WriteTo.Console(
        outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")

    .WriteTo.File(
        path: "Logs/matpoll-.log",
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 30,
        outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {Level:u3}] {Message:lj}{NewLine}{Exception}")

    .CreateLogger();

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog();

// ── Database ──────────────────────────────────────────────────────────────────
builder.Services.AddDbContext<AppDbContext>(opt =>
    opt.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// ── Services ──────────────────────────────────────────────────────────────────
builder.Services.AddScoped<AppRepository>();
builder.Services.AddSingleton<TokenService>();
builder.Services.AddSingleton<ActivityLogger>();   // structured event logger
// BatchCache is REMOVED — TrnStat=1 in DB is the lock now
builder.Services.AddHostedService<StallRecoveryService>();

// ── JWT ───────────────────────────────────────────────────────────────────────
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
            ValidateIssuer   = true, ValidIssuer   = "MatPoll",
            ValidateAudience = true, ValidAudience = "MatPollClient",
            ClockSkew        = TimeSpan.Zero
        };
        // Read token from cookie if no Authorization header
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
        Description = "Device polling server — login by DeviceID+MAC+IP, TypeMID-based dispatch"
    });
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name        = "Authorization",
        Type        = SecuritySchemeType.Http,
        Scheme      = "bearer",
        BearerFormat = "JWT",
        In          = ParameterLocation.Header
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

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "MatPoll v1");
    c.RoutePrefix = string.Empty;
});

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

Log.Information("MatPoll server started");
app.Run();
