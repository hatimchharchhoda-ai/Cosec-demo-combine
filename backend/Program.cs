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

namespace COSEC_demo
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            builder.Configuration.DecryptEncryptedValues();

            builder.Host.UseWindowsService();

            builder.Services.AddControllers();

            builder.Services.AddDbContext<AppDbContext>(options =>
                options.UseSqlServer(
                    builder.Configuration.GetConnectionString("DefaultConnection")));

            builder.Services.AddScoped<JwtHelper>();
            builder.Services.AddScoped<ILoginRepository, LoginRepository>();
            builder.Services.AddScoped<ILoginService, LoginService>();
            builder.Services.AddScoped<IDeviceRepository, DeviceRepository>();
            builder.Services.AddScoped<IDeviceService, DeviceService>();
            builder.Services.AddScoped<ICommTrnRepository, CommTrnRepository>();
            builder.Services.AddScoped<ICommTrnService, CommTrnService>();
            builder.Services.AddScoped<IUserRepository, UserRepository>();

            builder.Services.AddEndpointsApiExplorer();
            builder.Services.AddSwaggerGen();

            // FIX: Explicitly allow all localhost ports your Angular app may run on.
            // AllowAnyOrigin() fails silently when the server is unreachable;
            // explicit origins also allow AllowCredentials() if you ever need cookies.
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
                        .AllowCredentials()); // Safe now that origins are explicit
            });

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
                });

            builder.WebHost.UseUrls("http://0.0.0.0:5210");

            var app = builder.Build();

            app.UseSwagger();
            app.UseSwaggerUI();

            app.UseCors("AllowAngular");

            app.UseDefaultFiles();
            app.UseStaticFiles();

            app.UseAuthentication();
            app.UseAuthorization();

            app.MapControllers();

            app.MapFallbackToFile("index.html");

            // FIX: Wrap migration in try/catch so a DB error doesn't
            // crash the service silently with no log output.
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
                    // Don't rethrow — let the app start so Swagger/health endpoints work
                }
            }

            app.Run();
        }
    }
}