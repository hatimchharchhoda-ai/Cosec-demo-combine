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
builder.Services.AddScoped<AppRepository>();           // new instance per request
builder.Services.AddSingleton<TokenService>();         // one instance for all
builder.Services.AddSingleton<BatchCache>();           // one instance for all
builder.Services.AddHostedService<StallRecoveryService>(); // background cleanup

builder.Services.AddCors(options =>
{
    options.AddPolicy("AngularCors", policy =>
    {
        policy.WithOrigins("http://localhost:4200")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();   // 🔥 required for HttpOnly cookie
    });
});

// ── 3. JWT Authentication ─────────────────────────────────────────────────────
var secret = builder.Configuration["Jwt:Secret"]
    ?? throw new Exception("Jwt:Secret missing in appsettings.json");

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opt =>
    {
        opt.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey         = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)),
            ValidateIssuer           = true,
            ValidIssuer              = "MatPoll",
            ValidateAudience         = true,
            ValidAudience            = "MatPollClient",
            ClockSkew                = TimeSpan.Zero    // no grace period
        };

        // Read JWT from cookie "mat_auth" if no Authorization header present
        opt.Events = new JwtBearerEvents
        {
            OnMessageReceived = ctx =>
            {
                // Check cookie first
                var cookieToken = ctx.Request.Cookies["mat_auth"];
                if (!string.IsNullOrEmpty(cookieToken))
                    ctx.Token = cookieToken;
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddControllers();

// ── 4. Swagger ────────────────────────────────────────────────────────────────
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title   = "MatPoll API",
        Version = "v1",
        Description = "Polling server for Mat_CommTrn"
    });

    // Add Bearer token input box in Swagger
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name        = "Authorization",
        Type        = SecuritySchemeType.Http,
        Scheme      = "bearer",
        BearerFormat = "JWT",
        In          = ParameterLocation.Header,
        Description = "After login, paste your token here"
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {{
        new OpenApiSecurityScheme
        {
            Reference = new OpenApiReference
            {
                Type = ReferenceType.SecurityScheme,
                Id   = "Bearer"
            }
        },
        Array.Empty<string>()
    }});
});

// ── 5. Build pipeline ─────────────────────────────────────────────────────────
var app = builder.Build();

// Always show Swagger (remove UseSwagger in production if you want)
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "MatPoll API v1");
    c.RoutePrefix = string.Empty;   // Swagger opens at root: http://localhost:5000/
});

app.UseHttpsRedirection();

app.UseCors("AngularCors");

app.UseAuthentication();   // reads JWT from cookie or header
app.UseAuthorization();

app.MapControllers();

app.Run();
