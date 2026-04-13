using System.Text;
using MatPoll.Data;
using MatPoll.Repositories;
using MatPoll.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// ── 1. Database ───────────────────────────────────────────────────────────────
builder.Services.AddDbContext<AppDbContext>(opt =>
    opt.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// ── 2. App services ───────────────────────────────────────────────────────────
builder.Services.AddScoped<AppRepository>();
builder.Services.AddSingleton<TokenService>();
builder.Services.AddSingleton<BatchCache>();
builder.Services.AddHostedService<StallRecoveryService>();

// ── 3. CORS (Frontend Connection) ─────────────────────────────────────────────
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowFrontend", policy =>
    {
        policy
            .WithOrigins("http://localhost:4200") // Angular/React frontend URL
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials(); // REQUIRED for cookies
    });
});

// ── 4. JWT Authentication ─────────────────────────────────────────────────────
var secret = builder.Configuration["Jwt:Secret"]
    ?? throw new Exception("Jwt:Secret missing in appsettings.json");

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opt =>
    {
        opt.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)),
            ValidateIssuer = true,
            ValidIssuer = "MatPoll",
            ValidateAudience = true,
            ValidAudience = "MatPollClient",
            ClockSkew = TimeSpan.Zero
        };

        // Read JWT from cookie if not in header
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

// ── 5. Swagger ────────────────────────────────────────────────────────────────
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "MatPoll API",
        Version = "v1",
        Description = "Polling server for Mat_CommTrn"
    });

    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Paste JWT token here"
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {{
        new OpenApiSecurityScheme
        {
            Reference = new OpenApiReference
            {
                Type = ReferenceType.SecurityScheme,
                Id = "Bearer"
            }
        },
        Array.Empty<string>()
    }});
});

// ── 6. Build pipeline ─────────────────────────────────────────────────────────
var app = builder.Build();

// Swagger
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "MatPoll API v1");
    c.RoutePrefix = string.Empty;
});

// 🔥 IMPORTANT ORDER
app.UseCors("AllowFrontend");   // MUST come before auth

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();