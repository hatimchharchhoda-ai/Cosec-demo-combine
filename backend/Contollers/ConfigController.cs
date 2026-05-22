using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using Newtonsoft.Json.Linq;
using ConfigCrypto;
using COSEC_demo.Data;
using COSEC_demo.Entities;

namespace COSEC_demo.Contollers
{
    [ApiController]
    [Route("api/config")]
    public class ConfigController : ControllerBase
    {
        private readonly IConfiguration _configuration;
        private readonly IHostApplicationLifetime _lifetime;

        public ConfigController(IConfiguration configuration, IHostApplicationLifetime lifetime)
        {
            _configuration = configuration;
            _lifetime = lifetime;
        }

        [HttpGet("status")]
        public IActionResult GetStatus()
        {
            var connStr = _configuration.GetConnectionString("DefaultConnection");
            bool isConfigured = !string.IsNullOrWhiteSpace(connStr) && 
                                  !connStr.Contains("Server=;") && 
                                  !connStr.Contains("Database=;");
            return Ok(new { isConfigured });
        }

        [HttpPost("test")]
        public async Task<IActionResult> TestConnection([FromBody] ConfigTestRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Server) || string.IsNullOrWhiteSpace(request.Database))
            {
                return BadRequest(new { success = false, message = "Server and Database name are required." });
            }

            var connString = $"Server={request.Server};Database={request.Database};User Id={request.Username};Password={request.Password};TrustServerCertificate=True;MultipleActiveResultSets=true";

            try
            {
                using (var conn = new SqlConnection(connString))
                {
                    await conn.OpenAsync();
                }
                return Ok(new { success = true, dbExists = true });
            }
            catch (SqlException ex) when (ex.Number == 4060) // Cannot open database / Database does not exist
            {
                // Let's check if we can connect to master using the same server/credentials
                var masterConnString = $"Server={request.Server};Database=master;User Id={request.Username};Password={request.Password};TrustServerCertificate=True;MultipleActiveResultSets=true";
                try
                {
                    using (var conn = new SqlConnection(masterConnString))
                    {
                        await conn.OpenAsync();
                    }
                    return Ok(new { success = true, dbExists = false });
                }
                catch (Exception masterEx)
                {
                    return Ok(new { success = false, message = $"Database does not exist, and could not connect to 'master' database: {masterEx.Message}" });
                }
            }
            catch (Exception ex)
            {
                return Ok(new { success = false, message = ex.Message });
            }
        }

        [HttpPost("setup")]
        public async Task<IActionResult> SetupDatabase([FromBody] ConfigSetupRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Server) || string.IsNullOrWhiteSpace(request.Database))
            {
                return BadRequest(new { success = false, message = "Server and Database name are required." });
            }

            var connString = $"Server={request.Server};Database={request.Database};User Id={request.Username};Password={request.Password};TrustServerCertificate=True;MultipleActiveResultSets=true";

            try
            {
                // 1. Create database if it doesn't exist
                if (!request.DbExists)
                {
                    var masterConnString = $"Server={request.Server};Database=master;User Id={request.Username};Password={request.Password};TrustServerCertificate=True;MultipleActiveResultSets=true";
                    using (var conn = new SqlConnection(masterConnString))
                    {
                        await conn.OpenAsync();
                        using (var cmd = conn.CreateCommand())
                        {
                            var dbNameQuoted = request.Database.Replace("]", "]]");
                            cmd.CommandText = $"CREATE DATABASE [{dbNameQuoted}]";
                            await cmd.ExecuteNonQueryAsync();
                        }
                    }
                }

                // 2. Run EF Core migrations on AppDbContext using the new connection string
                var dbOptions = new DbContextOptionsBuilder<AppDbContext>()
                    .UseSqlServer(connString)
                    .Options;

                using (var context = new AppDbContext(dbOptions))
                {
                    await context.Database.MigrateAsync();

                    // 3. Create administrator user if it was a new database
                    if (!request.DbExists)
                    {
                        if (string.IsNullOrWhiteSpace(request.AdminUsername) || string.IsNullOrWhiteSpace(request.AdminPassword))
                        {
                            return BadRequest(new { success = false, message = "Admin username and password are required for a new database." });
                        }

                        // Check if admin user already exists
                        var exists = await context.LoginUsers.AnyAsync(x => x.LoginUserID == request.AdminUsername);
                        if (!exists)
                        {
                            var adminUser = new LoginUser
                            {
                                LoginUserID = request.AdminUsername,
                                LoginPassword = request.AdminPassword,
                                IsActive = 1
                            };
                            context.LoginUsers.Add(adminUser);
                            await context.SaveChangesAsync();
                        }
                    }
                }

                // 4. Encrypt the connection string
                string encryptedConnString = ConfigCryptoHelper.Encrypt(connString);

                // 5. Update appsettings.json in the AppContext.BaseDirectory
                var filePath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
                if (!System.IO.File.Exists(filePath))
                {
                    // Fallback to Current Directory
                    filePath = Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json");
                }

                if (System.IO.File.Exists(filePath))
                {
                    var jsonText = await System.IO.File.ReadAllTextAsync(filePath);
                    var jObject = JObject.Parse(jsonText);
                    
                    if (jObject["ConnectionStrings"] == null)
                    {
                        jObject["ConnectionStrings"] = new JObject();
                    }
                    
                    jObject["ConnectionStrings"]["DefaultConnection"] = encryptedConnString;

                    await System.IO.File.WriteAllTextAsync(filePath, jObject.ToString(Newtonsoft.Json.Formatting.Indented));
                }
                else
                {
                    return BadRequest(new { success = false, message = "Could not find appsettings.json file to update." });
                }

                // 6. Schedule process restart asynchronously
                _ = Task.Run(async () =>
                {
                    await Task.Delay(2000); // Give the response time to reach the client
                    _lifetime.StopApplication();
                });

                return Ok(new { success = true, message = "Database configured successfully. Service is restarting..." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = $"Failed to setup database: {ex.Message}" });
            }
        }

        [HttpGet("debug-path")]
        public IActionResult GetDebugPath()
        {
            return Ok(new {
                baseDir = AppContext.BaseDirectory,
                currentDir = Directory.GetCurrentDirectory(),
                connString = _configuration.GetConnectionString("DefaultConnection"),
                environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") 
                            ?? "NOT SET (defaults to Development)",
                allConnStrings = _configuration.GetSection("ConnectionStrings")
                                    .AsEnumerable()
                                    .ToDictionary(x => x.Key, x => x.Value),
                configSources = (HttpContext.RequestServices
                                    .GetService(typeof(IConfiguration)) as IConfigurationRoot)
                                    ?.Providers
                                    .Select(p => p.ToString())
                                    .ToList()
            });
        }
    }

    public class ConfigTestRequest
    {
        public string Server { get; set; }
        public string Database { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
    }

    public class ConfigSetupRequest
    {
        public string Server { get; set; }
        public string Database { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
        public bool DbExists { get; set; }
        public string AdminUsername { get; set; }
        public string AdminPassword { get; set; }
    }
}
