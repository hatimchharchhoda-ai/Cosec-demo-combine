using COSEC_demo.Data;
using COSEC_demo.Helpers;
using COSEC_demo.Repositories;
using COSEC_demo.Repositories.Interfaces;
using COSEC_demo.Services;
using COSEC_demo.Services.Interfaces;
using ConfigCrypto;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using Serilog;
using Serilog.Events;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace COSEC_demo
{
    public class Program
    {
        private static bool HasSink(LogEvent e, params string[] sinks)
        {
            if (!e.Properties.TryGetValue("Sink", out var v)) return false;
            var val = v.ToString().Trim('"');
            return sinks.Contains(val);
        }

        public static void Main(string[] args)
        {
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

            var builder = WebApplication.CreateBuilder(args);

            builder.Host.UseSerilog();
            builder.Host.UseWindowsService();

            // DECRYPT CONFIG (must come BEFORE anything reads config)
            builder.Configuration.DecryptEncryptedValues();

            var defaultConnection = builder.Configuration.GetConnectionString("DefaultConnection");
            bool isDbConfigured = !string.IsNullOrWhiteSpace(defaultConnection) && 
                                  !defaultConnection.Contains("Server=;") && 
                                  !defaultConnection.Contains("Database=;");

            // ── Database Contexts ────────────────────────────────────────────────────────
            builder.Services.AddDbContext<AppDbContext>(options =>
                options.UseSqlServer(
                    builder.Configuration.GetConnectionString("DefaultConnection")));



            // ── Original Backend Services ───────────────────────────────────────────────
            builder.Services.AddScoped<JwtHelper>();
            builder.Services.AddScoped<ILoginRepository, LoginRepository>();
            builder.Services.AddScoped<ILoginService, LoginService>();
            builder.Services.AddScoped<IDeviceRepository, DeviceRepository>();
            builder.Services.AddScoped<IDeviceService, DeviceService>();
            builder.Services.AddScoped<ICommTrnRepository, CommTrnRepository>();
            builder.Services.AddScoped<ICommTrnService, CommTrnService>();
            builder.Services.AddScoped<IUserRepository, UserRepository>();

            // ── MatPoll Merged Services ──────────────────────────────────────────────────
            builder.Services.AddScoped<MatPoll.Repositories.AppRepository>();
            builder.Services.AddSingleton<MatPoll.Services.TokenService>();
            builder.Services.AddSingleton<MatPoll.Services.ActivityLogger>();
            builder.Services.AddSingleton<MatPoll.Services.MetricsService>();
            builder.Services.AddSingleton<MatPoll.Services.GenetecSyncService>();

            if (isDbConfigured)
            {
                builder.Services.AddHostedService<MatPoll.Services.StallRecoveryService>();
                builder.Services.AddHostedService<MatPoll.Services.DeviceStatusService>();
                builder.Services.AddHostedService<MatPoll.Services.CommTrnRefillService>();
                builder.Services.AddHostedService(p => p.GetRequiredService<MatPoll.Services.MetricsService>());
                builder.Services.AddHostedService(p => p.GetRequiredService<MatPoll.Services.GenetecSyncService>());
            }

            builder.Services.AddControllers(options =>
            {
                options.Conventions.Add(new ActionConstraintConvention());
            });
            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen();

            // ── Response Compression & Health Checks ─────────────────────────────────────
            builder.Services.AddResponseCompression(opt =>
            {
                opt.EnableForHttps = true; 
            });

            var healthChecks = builder.Services.AddHealthChecks();
            if (isDbConfigured)
            {
                healthChecks.AddSqlServer(
                    builder.Configuration.GetConnectionString("DefaultConnection")!,
                    name:    "database",
                    failureStatus: Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Unhealthy);
            }

            // ── CORS Policy ──────────────────────────────────────────────────────────────
            builder.Services.AddCors(options =>
            {
                options.AddPolicy("AllowAngular",
                    policy => policy
                        .WithOrigins(
                            "http://localhost:4200",   // Angular dev server
                            "http://localhost:8080",   // Your current Angular origin
                            "http://localhost:5210"    // Same-origin requests
                        )
                        .AllowAnyHeader()
                        .AllowAnyMethod()
                        .AllowCredentials());
            });

            // ── Rate Limiting for MatPoll ────────────────────────────────────────────────
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
                        .GetRequiredService<MatPoll.Services.ActivityLogger>();

                    var ip   = ctx.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "";
                    var path = ctx.HttpContext.Request.Path;

                    logger.LogRateLimitExceeded(path, ip);

                    ctx.HttpContext.Response.StatusCode = 429;
                    await ctx.HttpContext.Response.WriteAsync(
                        "{\"error\":\"Too many requests. Please slow down.\"}", ct);
                };
            });

            builder.Services.AddRequestTimeouts(opt =>
            {
                opt.DefaultPolicy = new Microsoft.AspNetCore.Http.Timeouts.RequestTimeoutPolicy
                {
                    Timeout = TimeSpan.FromSeconds(30)  
                };
            });

            // ── Multi-Scheme Authentication ──────────────────────────────────────────────
            var jwtKey = builder.Configuration["JwtSettings:SecretKey"];

            builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddJwtBearer(options =>
                {
                    options.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuer = true,
                        ValidateAudience = true,
                        ValidateLifetime = true,
                        ValidateIssuerSigningKey = true,
                        ValidIssuer = builder.Configuration["JwtSettings:Issuer"],
                        ValidAudience = builder.Configuration["JwtSettings:Audience"],
                        IssuerSigningKey = new SymmetricSecurityKey(
                            Encoding.UTF8.GetBytes(jwtKey))
                    };

                    options.Events = new JwtBearerEvents
                    {
                        OnMessageReceived = context =>
                        {
                            context.Token = context.Request.Cookies["jwtToken"];
                            return Task.CompletedTask;
                        }
                    };
                })
                .AddJwtBearer("MatPollBearer", options =>
                {
                    var part1 = builder.Configuration["MatPollJwt:KeyPart1"]
                        ?? throw new Exception("MatPollJwt:KeyPart1 missing");
                    var part2 = builder.Configuration["MatPollJwt:KeyPart2"]
                        ?? throw new Exception("MatPollJwt:KeyPart2 missing");
                    var part3 = Environment.MachineName;
                    var combined   = $"{part1}:{part2}:{part3}";
                    var signingKey = new SymmetricSecurityKey(
                        Encoding.UTF8.GetBytes(combined));

                    options.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuerSigningKey = true,
                        IssuerSigningKey         = signingKey,
                        ValidateIssuer   = true, ValidIssuer   = "MatPoll",
                        ValidateAudience = true, ValidAudience = "MatPollClient",
                        ClockSkew        = TimeSpan.Zero
                    };

                    options.Events = new JwtBearerEvents
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
                            if (ctx.HttpContext.Request.Path.StartsWithSegments("/api/auth"))
                                return Task.CompletedTask;

                            var logger = ctx.HttpContext.RequestServices
                                .GetRequiredService<MatPoll.Services.ActivityLogger>();
                            var ip   = ctx.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "";
                            var path = ctx.HttpContext.Request.Path;

                            if (ctx.Exception is SecurityTokenExpiredException expEx)
                                logger.LogTokenExpired(path, ip, expEx.Expires);
                            else
                                logger.LogTokenInvalid(path, ip, ctx.Exception.Message);

                            return Task.CompletedTask;
                        },

                        OnChallenge = ctx =>
                        {
                            if (ctx.HttpContext.Request.Path.StartsWithSegments("/api/auth"))
                                return Task.CompletedTask;

                            if (ctx.AuthenticateFailure == null)
                            {
                                var logger = ctx.HttpContext.RequestServices
                                    .GetRequiredService<MatPoll.Services.ActivityLogger>();
                                var ip   = ctx.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "";
                                var path = ctx.HttpContext.Request.Path;

                                logger.LogTokenMissing(path, ip);
                            }
                            return Task.CompletedTask;
                        }
                    };
                });

            // ── Single-Port Web Host URL ─────────────────────────────────────────────────
            var port = builder.Configuration["Port"] ?? "5210";
            builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

            var app = builder.Build();

            // ── DB Warmup for MatPoll ─────────────────────────────────────────────────────
            if (isDbConfigured)
            {
                using (var scope = app.Services.CreateScope())
                {
                    try
                    {
                        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                        db.Database.ExecuteSqlRaw("SELECT 1");
                    }
                    catch (Exception ex)
                    {
                        Log.Warning($"DB Warmup failed: {ex.Message}");
                    }
                }
            }

            // ── Global Exception Handler for MatPoll ──────────────────────────────────────
            app.UseExceptionHandler(errApp =>
            {
                errApp.Run(async ctx =>
                {
                    var logger = ctx.RequestServices
                        .GetRequiredService<MatPoll.Services.ActivityLogger>();

                    var ex = ctx.Features
                        .Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error;

                    if (ex != null)
                        logger.LogException("UNHANDLED_CRASH", 0, ex);

                    ctx.Response.StatusCode  = 500;
                    ctx.Response.ContentType = "application/json";
                    await ctx.Response.WriteAsync("{\"error\":\"Internal server error\"}");
                });
            });

            app.UseSwagger();
            app.UseSwaggerUI();

            app.UseCors("AllowAngular");

            app.UseDefaultFiles();
            app.UseStaticFiles();

            app.UseResponseCompression();
            app.UseRateLimiter();
            app.UseRequestTimeouts();

            app.UseAuthentication();
            app.UseAuthorization();

            app.MapControllers();
            app.MapHealthChecks("/health");

            app.MapFallbackToFile("index.html");

            // Startup migrations for original DbContext
            if (isDbConfigured)
            {
                using (var scope = app.Services.CreateScope())
                {
                    var logger = scope.ServiceProvider
                        .GetRequiredService<ILogger<Program>>();
                    try
                    {
                        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                        db.Database.Migrate();
                        logger.LogInformation("Database migration applied successfully.");
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Database migration failed. " +
                            "Check your connection string and SQL Server availability.");
                    }
                }
            }

            Log.Information($"COSEC & MatPoll Merged Service started — Port {port} (Unified Backend & MatPoll)");

            app.Run();
        }
    }

    public class LoginActionConstraint : Microsoft.AspNetCore.Mvc.ActionConstraints.IActionConstraint
    {
        public int Order => 0;

        public bool Accept(Microsoft.AspNetCore.Mvc.ActionConstraints.ActionConstraintContext context)
        {
            var request = context.RouteContext.HttpContext.Request;

            if (!Microsoft.AspNetCore.Http.HttpMethods.IsPost(request.Method) || 
                request.Path.Value == null ||
                !request.Path.Value.EndsWith("/login", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            request.EnableBuffering();

            try
            {
                using (var reader = new System.IO.StreamReader(request.Body, leaveOpen: true))
                {
                    var body = reader.ReadToEndAsync().GetAwaiter().GetResult();
                    request.Body.Position = 0; // Reset body stream position

                    bool isDevice = body.Contains("MACAddr", StringComparison.OrdinalIgnoreCase) ||
                                    body.Contains("macaddress", StringComparison.OrdinalIgnoreCase) ||
                                    body.Contains("DeviceType", StringComparison.OrdinalIgnoreCase);

                    var actionName = context.CurrentCandidate.Action.DisplayName ?? "";
                    bool isDeviceAction = actionName.Contains("MatPoll");

                    return isDevice == isDeviceAction;
                }
            }
            catch
            {
                return true;
            }
        }
    }

    public class ActionConstraintConvention : Microsoft.AspNetCore.Mvc.ApplicationModels.IActionModelConvention
    {
        public void Apply(Microsoft.AspNetCore.Mvc.ApplicationModels.ActionModel action)
        {
            if (action.ActionName == "Login" && action.Controller.ControllerName == "Auth")
            {
                foreach (var selector in action.Selectors)
                {
                    selector.ActionConstraints.Add(new LoginActionConstraint());
                }
            }
        }
    }
}