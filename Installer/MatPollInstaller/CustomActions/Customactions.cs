using System;
using System.IO;
using System.Text;
using ConfigCrypto;
using Microsoft.Deployment.WindowsInstaller;

namespace CustomActions
{
    public class CustomActions
    {
        // ================================================================ //
        //  COSEC API – appsettings.json                                     //
        // ================================================================ //

        /// <summary>
        /// Deferred CA: writes an encrypted appsettings.json into COSECFOLDER.
        /// Sensitive values (connection string, JWT secret) are AES-256-GCM
        /// encrypted and stored as "ENC:<base64>" tokens.
        /// </summary>
        [CustomAction]
        public static ActionResult WriteAppSettings(Session session)
        {
            try
            {
                var data = new CustomActionData(session["CustomActionData"]);

                string installDir  = data["COSECFOLDER"];
                string dbServer    = data["DB_SERVER"];
                string dbName      = data["DB_NAME"];
                string dbUser      = data["DB_USER"];
                string dbPassword  = data["DB_PASSWORD"];
                string jwtSecret   = data["JWT_SECRET"];   // new property – see WXS

                // Build the plaintext connection string first, then encrypt it
                // as a single unit so it round-trips cleanly.
                string connString =
                    $"Server={dbServer};Database={dbName};User Id={dbUser};" +
                    $"Password={dbPassword};TrustServerCertificate=True;" +
                    $"MultipleActiveResultSets=true";

                // Encrypt sensitive fields
                string encConnString = ConfigCryptoHelper.Encrypt(connString);
                string encJwtSecret  = ConfigCryptoHelper.Encrypt(
                    string.IsNullOrWhiteSpace(jwtSecret)
                        ? "YOUR_SECRET_KEY_MIN_32_CHARS_LONG"
                        : jwtSecret);

                string json = $@"{{
                    ""Logging"": {{
                        ""LogLevel"": {{
                        ""Default"": ""Information"",
                        ""Microsoft.AspNetCore"": ""Warning""
                        }}
                    }},
                    ""AllowedHosts"": ""*"",
                    ""ConnectionStrings"": {{
                        ""DefaultConnection"": ""{EscapeJson(encConnString)}""
                    }},
                    ""JwtSettings"": {{
                        ""SecretKey"": ""{EscapeJson(encJwtSecret)}"",
                        ""Issuer"": ""COSEC_API"",
                        ""Audience"": ""COSEC_CLIENT"",
                        ""ExpiryMinutes"": 60
                    }},
                    ""Encrypted"": true
                    }}
                    ";
                string targetPath = Path.Combine(installDir, "appsettings.json");
                File.WriteAllText(targetPath, json, Encoding.UTF8);
                session.Log($"[WriteAppSettings] Written (encrypted) to: {targetPath}");
                return ActionResult.Success;
            }
            catch (Exception ex)
            {
                session.Log($"[WriteAppSettings] ERROR: {ex}");
                return ActionResult.Failure;
            }
        }

        [CustomAction]
        public static ActionResult RemoveAppSettings(Session session)
        {
            try
            {
                var data = new CustomActionData(session["CustomActionData"]);
                string targetPath = Path.Combine(data["COSECFOLDER"], "appsettings.json");
                if (File.Exists(targetPath)) File.Delete(targetPath);
                return ActionResult.Success;
            }
            catch (Exception ex)
            {
                session.Log($"[RemoveAppSettings] ERROR: {ex}");
                return ActionResult.Success; // non-fatal
            }
        }

        // ================================================================ //
        //  MatPoll Device Server – appsettings.json                         //
        // ================================================================ //

        /// <summary>
        /// Deferred CA: writes an encrypted appsettings.json into MATPOLLFOLDER.
        /// Sensitive fields: DB connection string, JWT key parts, Genetec credentials.
        /// Non-sensitive fields (polling tuning, log flags, Kestrel URL) are left
        /// in plaintext so they remain operator-editable without code changes.
        /// </summary>
        [CustomAction]
        public static ActionResult WriteMatPollAppSettings(Session session)
        {
            try
            {
                var data = new CustomActionData(session["CustomActionData"]);

                string installDir       = data["MATPOLLFOLDER"];
                string dbServer         = data["DB_SERVER"];
                string dbName           = data["DB_NAME"];
                string dbUser           = data["DB_USER"];
                string dbPassword       = data["DB_PASSWORD"];
                string jwtKeyPart1      = data["MATPOLL_JWT_KEY1"];
                string jwtKeyPart2      = data["MATPOLL_JWT_KEY2"];
                string genetecServer    = data["GENETEC_SERVER"];
                string genetecPort      = data["GENETEC_PORT"];
                string genetecUser      = data["GENETEC_USER"];
                string genetecPassword  = data["GENETEC_PASSWORD"];
                string genetecAppId     = data["GENETEC_APPID"];

                // Build and encrypt sensitive values
                string connString =
                    $"Server={dbServer};Database={dbName};User Id={dbUser};" +
                    $"Password={dbPassword};TrustServerCertificate=True;";

                string encConn          = ConfigCryptoHelper.Encrypt(connString);
                string encJwtKey1       = ConfigCryptoHelper.Encrypt(
                    string.IsNullOrWhiteSpace(jwtKeyPart1) ? "matpoll-auth" : jwtKeyPart1);
                string encJwtKey2       = ConfigCryptoHelper.Encrypt(
                    string.IsNullOrWhiteSpace(jwtKeyPart2) ? "device-polling-2026" : jwtKeyPart2);
                string encGenetecUser   = ConfigCryptoHelper.Encrypt(
                    string.IsNullOrWhiteSpace(genetecUser) ? "Admin" : genetecUser);
                string encGenetecPass   = ConfigCryptoHelper.Encrypt(
                    string.IsNullOrWhiteSpace(genetecPassword) ? "" : genetecPassword);
                string encGenetecAppId  = ConfigCryptoHelper.Encrypt(
                    string.IsNullOrWhiteSpace(genetecAppId) ? "" : genetecAppId);

                string json = $@"{{
                    ""ConnectionStrings"": {{
                        ""DefaultConnection"": ""{EscapeJson(encConn)}""
                    }},
                    ""Jwt"": {{
                        ""KeyPart1"": ""{EscapeJson(encJwtKey1)}"",
                        ""KeyPart2"": ""{EscapeJson(encJwtKey2)}"",
                        ""ExpirySeconds"": 20
                    }},
                    ""PollingSettings"": {{
                        ""BunchSize"": ""10"",
                        ""AckTimeoutWarningSeconds"": ""30"",
                        ""StallTimeoutMinutes"": ""1"",
                        ""DeviceOfflineCheckMinutes"": 2,
                        ""DeviceOfflineTimeoutMinutes"": 1,
                        ""CommTrnRefillIntervalHours"": 1,
                        ""CommTrnRefillThreshold"": 50,
                        ""PollingSettings:CommTrnRowsPerRefill"": 100
                    }},
                    ""Genetec"": {{
                        ""Server"": ""{EscapeJson(string.IsNullOrWhiteSpace(genetecServer) ? "192.168.27.115" : genetecServer)}"",
                        ""Port"": ""{EscapeJson(string.IsNullOrWhiteSpace(genetecPort) ? "4590" : genetecPort)}"",
                        ""BaseUri"": ""WebSdk"",
                        ""Username"": ""{EscapeJson(encGenetecUser)}"",
                        ""Password"": ""{EscapeJson(encGenetecPass)}"",
                        ""ApplicationId"": ""{EscapeJson(encGenetecAppId)}"",
                        ""UseHttps"": false,
                        ""SyncIntervalMinutes"": 30
                    }},
                    ""TestingLog"": false,
                    ""Logging"": {{
                        ""LogLevel"": {{
                        ""Default"": ""Information"",
                        ""Microsoft.AspNetCore"": ""Warning""
                        }}
                    }},
                    ""Kestrel"": {{
                        ""Endpoints"": {{
                        ""Http"": {{
                            ""Url"": ""http://0.0.0.0:5000""
                        }}
                        }}
                    }},
                    ""LogSettings"": {{
                        ""InfoLog"": true,
                        ""DebugLog"": true,
                        ""ErrorLog"": true,
                        ""TestingLog"": false
                    }},
                    ""AllowedHosts"": ""*"",
                    ""Encrypted"": true
                    }}
                    ";
                string targetPath = Path.Combine(installDir, "appsettings.json");
                File.WriteAllText(targetPath, json, Encoding.UTF8);
                session.Log($"[WriteMatPollAppSettings] Written (encrypted) to: {targetPath}");
                return ActionResult.Success;
            }
            catch (Exception ex)
            {
                session.Log($"[WriteMatPollAppSettings] ERROR: {ex}");
                return ActionResult.Failure;
            }
        }

        [CustomAction]
        public static ActionResult RemoveMatPollAppSettings(Session session)
        {
            try
            {
                var data = new CustomActionData(session["CustomActionData"]);
                string targetPath = Path.Combine(data["MATPOLLFOLDER"], "appsettings.json");
                if (File.Exists(targetPath)) File.Delete(targetPath);
                return ActionResult.Success;
            }
            catch (Exception ex)
            {
                session.Log($"[RemoveMatPollAppSettings] ERROR: {ex}");
                return ActionResult.Success;
            }
        }

        // ---------------------------------------------------------------- //

        private static string EscapeJson(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return value
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\r", "\\r")
                .Replace("\n", "\\n");
        }
    }
}