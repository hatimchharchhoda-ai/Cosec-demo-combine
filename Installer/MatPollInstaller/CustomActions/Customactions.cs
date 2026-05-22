using System;
using System.IO;
using System.Text;
using System.ServiceProcess;
using ConfigCrypto;
using Microsoft.Deployment.WindowsInstaller;
using FileAttributes = System.IO.FileAttributes;

namespace CustomActions
{
    public class CustomActions
    {
        [CustomAction]
        public static ActionResult StopAndRemoveService(Session session)
        {
            const string serviceName = "MATINTService";
            try
            {
                // 1. Kill the hosted process by name FIRST (handles locked EXE)
                try
                {
                    foreach (var proc in System.Diagnostics.Process.GetProcessesByName("COSEC-demo"))
                    {
                        session.Log($"[StopAndRemoveService] Killing process {proc.Id}...");
                        proc.Kill();
                        proc.WaitForExit(5000);
                    }
                }
                catch (Exception ex)
                {
                    session.Log($"[StopAndRemoveService] Kill process warning: {ex.Message}");
                }

                // 2. Stop via SCM
                try
                {
                    using (var sc = new ServiceController(serviceName))
                    {
                        if (sc.Status != ServiceControllerStatus.Stopped &&
                            sc.Status != ServiceControllerStatus.StopPending)
                        {
                            session.Log($"[StopAndRemoveService] Stopping {serviceName}...");
                            sc.Stop();
                            sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(30));
                            session.Log($"[StopAndRemoveService] Stopped.");
                        }
                    }
                }
                catch (Exception ex)
                {
                    session.Log($"[StopAndRemoveService] Stop warning (may not exist): {ex.Message}");
                }

                // 3. Delete via sc.exe — this is the authoritative removal
                session.Log($"[StopAndRemoveService] Deleting {serviceName} from SCM...");
                RunProcess("sc.exe", $"delete {serviceName}", session);

                // 4. Brief wait for SCM to release file handle
                System.Threading.Thread.Sleep(2000);

                return ActionResult.Success;
            }
            catch (Exception ex)
            {
                session.Log($"[StopAndRemoveService] ERROR: {ex}");
                return ActionResult.Success; // non-fatal
            }
        }

        [CustomAction]
        public static ActionResult CleanupInstallFolder(Session session)
        {
            try
            {
                var data = new CustomActionData(session["CustomActionData"]);
                string installDir = data["COSECFOLDER"].TrimEnd('\\', '/');

                if (!Directory.Exists(installDir))
                {
                    session.Log($"[CleanupInstallFolder] Folder does not exist, nothing to clean: {installDir}");
                    return ActionResult.Success;
                }

                session.Log($"[CleanupInstallFolder] Cleaning folder: {installDir}");

                // Delete all files except appsettings.json
                foreach (string file in Directory.GetFiles(installDir, "*", SearchOption.TopDirectoryOnly))
                {
                    string fileName = Path.GetFileName(file);
                    if (string.Equals(fileName, "appsettings.json", StringComparison.OrdinalIgnoreCase))
                    {
                        session.Log($"[CleanupInstallFolder] Preserving: {file}");
                        continue;
                    }
                    try
                    {
                        File.SetAttributes(file, FileAttributes.Normal); // clear read-only if set
                        File.Delete(file);
                        session.Log($"[CleanupInstallFolder] Deleted file: {file}");
                    }
                    catch (Exception ex)
                    {
                        session.Log($"[CleanupInstallFolder] Could not delete file {file}: {ex.Message}");
                    }
                }

                // Delete all subdirectories recursively
                foreach (string dir in Directory.GetDirectories(installDir))
                {
                    try
                    {
                        Directory.Delete(dir, recursive: true);
                        session.Log($"[CleanupInstallFolder] Deleted directory: {dir}");
                    }
                    catch (Exception ex)
                    {
                        session.Log($"[CleanupInstallFolder] Could not delete directory {dir}: {ex.Message}");
                    }
                }

                // The folder itself will have only appsettings.json left — leave it.
                // If somehow empty (appsettings didn't exist), remove it too.
                if (Directory.GetFiles(installDir).Length == 0 &&
                    Directory.GetDirectories(installDir).Length == 0)
                {
                    try
                    {
                        Directory.Delete(installDir);
                        session.Log($"[CleanupInstallFolder] Removed empty install folder: {installDir}");
                    }
                    catch (Exception ex)
                    {
                        session.Log($"[CleanupInstallFolder] Could not remove install folder: {ex.Message}");
                    }
                }

                return ActionResult.Success;
            }
            catch (Exception ex)
            {
                session.Log($"[CleanupInstallFolder] ERROR: {ex}");
                return ActionResult.Success; // non-fatal
            }
        }

        private static void RunProcess(string exe, string args, Session session)
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName               = exe,
                Arguments              = args,
                UseShellExecute        = false,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                CreateNoWindow         = true
            };
            using (var proc = System.Diagnostics.Process.Start(psi))
            {
                string output = proc.StandardOutput.ReadToEnd();
                string error  = proc.StandardError.ReadToEnd();
                proc.WaitForExit();
                session.Log($"[RunProcess] {exe} {args} => exit {proc.ExitCode}: {output} {error}");
            }
        }

        // ================================================================ //
        //  COSEC API – appsettings.json                                     //
        // ================================================================ //

        [CustomAction]
        public static ActionResult WriteAppSettings(Session session)
        {
            try
            {
                var data = new CustomActionData(session["CustomActionData"]);
                string installDir = data["COSECFOLDER"];
                string targetPath = Path.Combine(installDir, "appsettings.json");

                if (File.Exists(targetPath))
                {
                    // Check if already encrypted — if the file has "ENC:" it was
                    // written by us before; skip to preserve user config.
                    string existing = File.ReadAllText(targetPath);
                    if (existing.Contains("\"Encrypted\": true") || existing.Contains("ENC:"))
                    {
                        session.Log($"[WriteAppSettings] Encrypted appsettings.json already exists — skipping.");
                        return ActionResult.Success;
                    }
                    session.Log($"[WriteAppSettings] Plaintext appsettings.json found — overwriting with encrypted version.");
                }

                session.Log($"[WriteAppSettings] Writing encrypted appsettings.json to: {targetPath}");

                // Safe reads — fall back to defaults rather than throwing
                string dbServer        = SafeGet(data, "DB_SERVER");
                string dbName          = SafeGet(data, "DB_NAME");
                string dbUser          = SafeGet(data, "DB_USER");
                string dbPassword      = SafeGet(data, "DB_PASSWORD");
                string jwtSecret       = SafeGet(data, "JWT_SECRET");
                string jwtKeyPart1     = SafeGet(data, "MATPOLL_JWT_KEY1");
                string jwtKeyPart2     = SafeGet(data, "MATPOLL_JWT_KEY2");
                string genetecServer   = SafeGet(data, "GENETEC_SERVER");
                string genetecPort     = SafeGet(data, "GENETEC_PORT");
                string genetecUser     = SafeGet(data, "GENETEC_USER");
                string genetecPassword = SafeGet(data, "GENETEC_PASSWORD");
                string genetecAppId    = SafeGet(data, "GENETEC_APPID");

                // --- Encrypt ALL sensitive fields ---
                string encConnString = "";
                if (!string.IsNullOrWhiteSpace(dbServer) && !string.IsNullOrWhiteSpace(dbName))
                {
                    string connString =
                        $"Server={dbServer};Database={dbName};User Id={dbUser};" +
                        $"Password={dbPassword};TrustServerCertificate=True;" +
                        $"MultipleActiveResultSets=true";
                    encConnString = ConfigCryptoHelper.Encrypt(connString);
                }

                string encJwtSecret    = ConfigCryptoHelper.Encrypt(
                    string.IsNullOrWhiteSpace(jwtSecret) ? "YOUR_SECRET_KEY_MIN_32_CHARS_LONG" : jwtSecret);
                string encJwtKey1      = ConfigCryptoHelper.Encrypt(
                    string.IsNullOrWhiteSpace(jwtKeyPart1) ? "matpoll-auth" : jwtKeyPart1);
                string encJwtKey2      = ConfigCryptoHelper.Encrypt(
                    string.IsNullOrWhiteSpace(jwtKeyPart2) ? "device-polling-2026" : jwtKeyPart2);
                string encGenetecUser  = ConfigCryptoHelper.Encrypt(
                    string.IsNullOrWhiteSpace(genetecUser) ? "Admin" : genetecUser);
                string encGenetecPass  = ConfigCryptoHelper.Encrypt(
                    string.IsNullOrWhiteSpace(genetecPassword) ? "" : genetecPassword);
                string encGenetecAppId = ConfigCryptoHelper.Encrypt(
                    string.IsNullOrWhiteSpace(genetecAppId) ? "" : genetecAppId);

                // BUG FIX: Server and Port were previously written RAW — now encrypted too
                string encGenetecServer = ConfigCryptoHelper.Encrypt(
                    string.IsNullOrWhiteSpace(genetecServer) ? "192.168.27.115" : genetecServer);
                string encGenetecPort   = ConfigCryptoHelper.Encrypt(
                    string.IsNullOrWhiteSpace(genetecPort) ? "4590" : genetecPort);

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
                    ""MatPollJwt"": {{
                        ""KeyPart1"": ""{EscapeJson(encJwtKey1)}"",
                        ""KeyPart2"": ""{EscapeJson(encJwtKey2)}"",
                        ""ExpirySeconds"": 20,
                        ""ExpiryMinutes"": 60
                    }},
                    ""PollingSettings"": {{
                        ""BunchSize"": ""10"",
                        ""AckTimeoutWarningSeconds"": ""30"",
                        ""StallTimeoutMinutes"": ""1"",
                        ""DeviceOfflineCheckMinutes"": 2,
                        ""DeviceOfflineTimeoutMinutes"": 1,
                        ""CommTrnRefillIntervalHours"": 1,
                        ""CommTrnRefillThreshold"": 50,
                        ""CommTrnRowsPerRefill"": 100
                    }},
                    ""Genetec"": {{
                        ""Server"": ""{EscapeJson(encGenetecServer)}"",
                        ""Port"": ""{EscapeJson(encGenetecPort)}"",
                        ""BaseUri"": ""WebSdk"",
                        ""Username"": ""{EscapeJson(encGenetecUser)}"",
                        ""Password"": ""{EscapeJson(encGenetecPass)}"",
                        ""ApplicationId"": ""{EscapeJson(encGenetecAppId)}"",
                        ""UseHttps"": false,
                        ""SyncIntervalMinutes"": 30
                    }},
                    ""TestingLog"": false,
                    ""LogSettings"": {{
                        ""InfoLog"": true,
                        ""DebugLog"": true,
                        ""ErrorLog"": true,
                        ""TestingLog"": false
                    }},
                    ""Encrypted"": true
                    }}";
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

        private static string SafeGet(CustomActionData data, string key)
        {
            try { return data.ContainsKey(key) ? data[key] : string.Empty; }
            catch { return string.Empty; }
        }

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