using MatGenServer.Repositories.Interfaces;
using MatGenServer.Repositories;
using MatGenServer.Services;
using MatGenServer.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// -------------------- Add Services --------------------

// ✅ DB Context
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// ✅ Controllers
builder.Services.AddControllers();

// ✅ Swagger
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// ✅ Repository
builder.Services.AddScoped<ICommTrnRepository, CommTrnRepository>();

// ✅ Background Service
builder.Services.AddHostedService<StallRecoveryService>();

// -------------------- Build App --------------------

var app = builder.Build();

// -------------------- Middleware --------------------

// ✅ Swagger UI (only in Development)
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Optional
app.UseHttpsRedirection();

app.UseAuthorization();

app.MapGet("/", () => "Server is running...");

app.MapControllers();

// -------------------- Run --------------------

app.Run();