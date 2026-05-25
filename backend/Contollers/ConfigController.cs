using System;
using System.IO;
using System.Linq;
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

        // ─── Helpers ────────────────────────────────────────────────────────────────

        /// <summary>Builds a connection string safely, supporting both SQL auth and Windows auth.</summary>
        private static string BuildConnString(string server, string database, string? username, string? password)
        {
            // Use Windows auth when no username is supplied
            if (string.IsNullOrWhiteSpace(username))
                return $"Server={server};Database={database};Integrated Security=True;TrustServerCertificate=True;MultipleActiveResultSets=true";

            return $"Server={server};Database={database};User Id={username};Password={password ?? ""};TrustServerCertificate=True;MultipleActiveResultSets=true";
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
            // ── Validate inputs ──────────────────────────────────────────────────────
            if (string.IsNullOrWhiteSpace(request?.Server))
                return BadRequest(new { success = false, message = "STEP [VALIDATE]: Server name is required." });

            if (string.IsNullOrWhiteSpace(request.Database))
                return BadRequest(new { success = false, message = "STEP [VALIDATE]: Database name is required." });

            // ── Try connecting directly to the target database ───────────────────────
            var targetConnStr = BuildConnString(request.Server, request.Database, request.Username, request.Password);

            try
            {
                using var conn = new SqlConnection(targetConnStr);
                await conn.OpenAsync();
                // Connection succeeded → database exists and credentials are valid
                return Ok(new { success = true, dbExists = true });
            }
            catch (SqlException ex) when (ex.Number == 4060 || ex.Number == 911 || ex.Number == 18456)
            {
                // 4060 = cannot open database  |  911 = DB not found  |  18456 = login failed
                // Fall through to check master
            }
            catch (Exception ex)
            {
                // Unexpected error (network, bad server name, malformed string, etc.)
                return Ok(new
                {
                    success = false,
                    message = $"STEP [CONNECT TO TARGET DB]: Unexpected error — {ex.GetType().Name}: {ex.Message}"
                });
            }

            // ── Try master to validate credentials and check if DB exists ────────────
            var masterConnStr = BuildConnString(request.Server, "master", request.Username, request.Password);

            try
            {
                using var masterConn = new SqlConnection(masterConnStr);
                await masterConn.OpenAsync();

                // Check if the target database exists in sys.databases
                using var cmd = masterConn.CreateCommand();
                cmd.CommandText = "SELECT COUNT(1) FROM sys.databases WHERE name = @name";
                cmd.Parameters.AddWithValue("@name", request.Database);
                var count = (int)(await cmd.ExecuteScalarAsync() ?? 0);

                return Ok(new { success = true, dbExists = count > 0 });
            }
            catch (SqlException ex) when (ex.Number == 18456)
            {
                return Ok(new
                {
                    success = false,
                    message = $"STEP [CONNECT TO MASTER]: Login failed for user '{request.Username}'. Check your SQL Server username and password. (SQL Error 18456)"
                });
            }
            catch (SqlException ex)
            {
                return Ok(new
                {
                    success = false,
                    message = $"STEP [CONNECT TO MASTER]: SQL error {ex.Number} — {ex.Message}"
                });
            }
            catch (Exception ex)
            {
                return Ok(new
                {
                    success = false,
                    message = $"STEP [CONNECT TO MASTER]: Unexpected error — {ex.GetType().Name}: {ex.Message}"
                });
            }
        }

        [HttpPost("setup")]
        public async Task<IActionResult> SetupDatabase([FromBody] ConfigSetupRequest request)
        {
            // ── Validate inputs ──────────────────────────────────────────────────────
            if (string.IsNullOrWhiteSpace(request?.Server))
                return BadRequest(new { success = false, message = "STEP [VALIDATE]: Server name is required." });

            if (string.IsNullOrWhiteSpace(request.Database))
                return BadRequest(new { success = false, message = "STEP [VALIDATE]: Database name is required." });

            var targetConnStr  = BuildConnString(request.Server, request.Database, request.Username, request.Password);
            var masterConnStr  = BuildConnString(request.Server, "master",          request.Username, request.Password);
            var dbNameQuoted   = request.Database.Replace("]", "]]");   // safe for [ ] identifier quoting
            var dbNameSafe     = request.Database.Replace("'", "''");   // safe for string literals

            // ── STEP 1: Create database if it does not exist ─────────────────────────
            if (!request.DbExists)
            {
                if (string.IsNullOrWhiteSpace(request.AdminUsername) || string.IsNullOrWhiteSpace(request.AdminPassword))
                    return BadRequest(new { success = false, message = "STEP [VALIDATE ADMIN]: Admin username and password are required when creating a new database." });

                try
                {
                    using var masterConn = new SqlConnection(masterConnStr);
                    await masterConn.OpenAsync();

                    using var cmd = masterConn.CreateCommand();

                    // 1a. Create the database if not exists
                    cmd.CommandText = $@"
                        IF NOT EXISTS (SELECT name FROM sys.databases WHERE name = N'{dbNameSafe}')
                        BEGIN
                            CREATE DATABASE [{dbNameQuoted}]
                        END";
                    await cmd.ExecuteNonQueryAsync();

                    // Give SQL Server time to finish initializing the new database files
                    await Task.Delay(1500);

                    // 1b. Grant access only for non-sa, non-sysadmin logins
                    var loginSafe = request.Username!.Replace("'", "''");

                    cmd.CommandText = $@"
                        DECLARE @isSysAdmin INT = IS_SRVROLEMEMBER('sysadmin', N'{loginSafe}');
                        
                        IF @isSysAdmin = 0  -- only non-sysadmins need explicit DB user creation
                        BEGIN
                            USE [{dbNameQuoted}];
                            IF NOT EXISTS (
                                SELECT 1 FROM sys.database_principals 
                                WHERE name = N'{loginSafe}'
                            )
                            BEGIN
                                CREATE USER [{loginSafe}] FOR LOGIN [{loginSafe}];
                            END
                            ALTER ROLE [db_owner] ADD MEMBER [{loginSafe}];
                            USE [master];
                        END";
                    await cmd.ExecuteNonQueryAsync();
                }
                catch (SqlException ex)
                {
                    return StatusCode(500, new
                    {
                        success = false,
                        message = $"STEP [CREATE DATABASE]: SQL error {ex.Number} — {ex.Message}"
                    });
                }
                catch (Exception ex)
                {
                    return StatusCode(500, new
                    {
                        success = false,
                        message = $"STEP [CREATE DATABASE]: {ex.GetType().Name} — {ex.Message}"
                    });
                }
            }

            // ── STEP 2: Verify we can connect to the target database ─────────────────
            try
            {
                using var testConn = new SqlConnection(targetConnStr);
                await testConn.OpenAsync();
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = $"STEP [VERIFY CONNECTION]: Cannot connect to '{request.Database}' after creation — {ex.GetType().Name}: {ex.Message}"
                });
            }

            // ── STEP 3: Run EF Core migrations ───────────────────────────────────────
            try
            {
                var dbOptions = new DbContextOptionsBuilder<AppDbContext>()
                    .UseSqlServer(targetConnStr)
                    .Options;

                using var context = new AppDbContext(dbOptions);

                // List pending migrations for diagnostic logging
                var pending = (await context.Database.GetPendingMigrationsAsync()).ToList();
                if (!pending.Any())
                {
                    // No pending migrations — maybe migrations folder is missing entirely
                    // Try EnsureCreated as fallback to at least create the schema
                    await context.Database.EnsureCreatedAsync();
                }
                else
                {
                    await context.Database.MigrateAsync();
                }

                // ── STEP 4: Seed admin user for new databases ─────────────────────────
                if (!request.DbExists)
                {
                    try
                    {
                        var exists = await context.LoginUsers.AnyAsync(x => x.LoginUserID == request.AdminUsername);
                        if (!exists)
                        {
                            context.LoginUsers.Add(new LoginUser
                            {
                                LoginUserID    = request.AdminUsername,
                                LoginPassword  = request.AdminPassword,
                                IsActive       = 1
                            });
                            await context.SaveChangesAsync();
                        }
                    }
                    catch (Exception ex)
                    {
                        return StatusCode(500, new
                        {
                            success = false,
                            message = $"STEP [SEED ADMIN USER]: Tables may not have been created — {ex.GetType().Name}: {ex.Message}. Pending migrations were: [{string.Join(", ", pending)}]"
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = $"STEP [RUN MIGRATIONS]: {ex.GetType().Name} — {ex.Message}"
                });
            }

            // ── STEP 5: Encrypt and persist the connection string ────────────────────
            try
            {
                string encryptedConnStr = ConfigCryptoHelper.Encrypt(targetConnStr);

                var filePath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
                if (!System.IO.File.Exists(filePath))
                    filePath = Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json");

                if (!System.IO.File.Exists(filePath))
                    return BadRequest(new { success = false, message = "STEP [SAVE CONFIG]: Could not find appsettings.json in BaseDirectory or CurrentDirectory." });

                var jsonText = await System.IO.File.ReadAllTextAsync(filePath);
                var jObject  = JObject.Parse(jsonText);

                jObject["ConnectionStrings"] ??= new JObject();
                jObject["ConnectionStrings"]!["DefaultConnection"] = encryptedConnStr;

                await System.IO.File.WriteAllTextAsync(filePath, jObject.ToString(Newtonsoft.Json.Formatting.Indented));
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    success = false,
                    message = $"STEP [SAVE CONFIG]: {ex.GetType().Name} — {ex.Message}"
                });
            }

            // ── STEP 6: Restart the application ─────────────────────────────────────
            _ = Task.Run(async () =>
            {
                await Task.Delay(2000);
                _lifetime.StopApplication();
            });

            return Ok(new { success = true, message = "Database configured successfully. Service is restarting..." });
        }

        [HttpGet("debug-path")]
        public IActionResult GetDebugPath()
        {
            return Ok(new
            {
                baseDir        = AppContext.BaseDirectory,
                currentDir     = Directory.GetCurrentDirectory(),
                connString     = _configuration.GetConnectionString("DefaultConnection"),
                environment    = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "NOT SET (defaults to Development)",
                allConnStrings = _configuration.GetSection("ConnectionStrings").AsEnumerable().ToDictionary(x => x.Key, x => x.Value),
                configSources  = (HttpContext.RequestServices.GetService(typeof(IConfiguration)) as IConfigurationRoot)
                                    ?.Providers.Select(p => p.ToString()).ToList()
            });
        }
    }

    public class ConfigTestRequest
    {
        public string?  Server    { get; set; }
        public string?  Database  { get; set; }
        public string?  Username  { get; set; }
        public string?  Password  { get; set; }
    }

    public class ConfigSetupRequest
    {
        public string?  Server         { get; set; }
        public string?  Database       { get; set; }
        public string?  Username       { get; set; }
        public string?  Password       { get; set; }
        public bool     DbExists       { get; set; }
        public string?  AdminUsername  { get; set; }
        public string?  AdminPassword  { get; set; }
    }
}