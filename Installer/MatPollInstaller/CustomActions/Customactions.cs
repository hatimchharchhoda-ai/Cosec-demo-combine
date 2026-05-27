using System;
using System.IO;
using System.Text;
using System.ServiceProcess;
using ConfigCrypto;
using Microsoft.Deployment.WindowsInstaller;
using FileAttributes = System.IO.FileAttributes;
using Microsoft.Web.Administration;

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
        public static ActionResult RemoveIISSiteAndPool(Session session)
        {
            try
            {
                // ── Remove IIS Site ──────────────────────────────────────────
                using (ServerManager mgr = new ServerManager())
                {
                    // Remove site named "MATINTWeb"
                    Site site = mgr.Sites["MATINTWeb"];
                    if (site != null)
                    {
                        session.Log("RemoveIISSiteAndPool: stopping site MATINTWeb");
                        site.Stop();
                        mgr.Sites.Remove(site);
                        session.Log("RemoveIISSiteAndPool: site removed");
                    }
                    else
                    {
                        session.Log("RemoveIISSiteAndPool: site MATINTWeb not found, skipping");
                    }

                    // Remove app pool named "COSECAppPool"
                    ApplicationPool pool = mgr.ApplicationPools["COSECAppPool"];
                    if (pool != null)
                    {
                        session.Log("RemoveIISSiteAndPool: stopping app pool COSECAppPool");
                        pool.Stop();
                        mgr.ApplicationPools.Remove(pool);
                        session.Log("RemoveIISSiteAndPool: app pool removed");
                    }
                    else
                    {
                        session.Log("RemoveIISSiteAndPool: app pool COSECAppPool not found, skipping");
                    }

                    mgr.CommitChanges();
                    session.Log("RemoveIISSiteAndPool: IIS changes committed");
                }
            }
            catch (Exception ex)
            {
                session.Log($"RemoveIISSiteAndPool error (non-fatal): {ex.Message}");
            }

            return ActionResult.Success;
        }

        [CustomAction]
        public static ActionResult CleanupInstallFolder(Session session)
        {
            try
            {
                string data = session.CustomActionData.ToString();
                session.Log($"CleanupInstallFolder: CustomActionData = {data}");

                string cosecFolder = null;

                foreach (var part in data.Split(';'))
                {
                    var kv = part.Split(new char[] { '=' }, 2);
                    if (kv.Length == 2 && kv[0].Trim() == "COSECFOLDER")
                    {
                        cosecFolder = kv[1].Trim();
                        break;
                    }
                }

                session.Log($"CleanupInstallFolder: resolved path = {cosecFolder}");

                if (!string.IsNullOrEmpty(cosecFolder) && Directory.Exists(cosecFolder))
                {
                    foreach (var file in Directory.GetFiles(cosecFolder, "*", SearchOption.AllDirectories))
                    {
                        if (!file.EndsWith("appsettings.json", StringComparison.OrdinalIgnoreCase))
                        {
                            File.Delete(file);
                            session.Log($"CleanupInstallFolder: deleted {file}");
                        }
                    }

                    // Remove empty subdirectories
                    foreach (var dir in Directory.GetDirectories(cosecFolder))
                    {
                        if (Directory.GetFiles(dir, "*", SearchOption.AllDirectories).Length == 0)
                        {
                            Directory.Delete(dir, recursive: true);
                            session.Log($"CleanupInstallFolder: removed empty dir {dir}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                session.Log($"CleanupInstallFolder error (non-fatal): {ex.Message}");
            }

            return ActionResult.Success;
        }

        [CustomAction]
        public static ActionResult CleanupWebFolder(Session session)
        {
            try
            {
                // Deferred CAs can only read their CustomActionData property
                string data = session.CustomActionData.ToString();
                session.Log($"CleanupWebFolder: CustomActionData = {data}");

                string webFolder = null;

                foreach (var part in data.Split(';'))
                {
                    var kv = part.Split(new char[] { '=' }, 2); // max 2 splits, path may contain =
                    if (kv.Length == 2 && kv[0].Trim() == "COSECWEBFOLDER")
                    {
                        webFolder = kv[1].Trim();
                        break;
                    }
                }

                session.Log($"CleanupWebFolder: resolved path = {webFolder}");

                if (!string.IsNullOrEmpty(webFolder) && Directory.Exists(webFolder))
                {
                    session.Log($"CleanupWebFolder: deleting {webFolder}");
                    Directory.Delete(webFolder, recursive: true);
                    session.Log("CleanupWebFolder: deleted successfully");
                }
                else
                {
                    session.Log("CleanupWebFolder: folder not found or path empty, skipping");
                }
            }
            catch (Exception ex)
            {
                session.Log($"CleanupWebFolder error (non-fatal): {ex.Message}");
            }

            return ActionResult.Success;
        }

        [CustomAction]
        public static ActionResult InstallUrlRewrite(Session session)
        {
            try
            {
                session.Log("InstallUrlRewrite: starting...");

                // Check registry — skip if already installed
                using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\IIS Extensions\URL Rewrite"))
                {
                    if (key != null)
                    {
                        session.Log("URL Rewrite already installed. Skipping.");
                        return ActionResult.Success;
                    }
                }

                // CustomActionData is the full property value set by setter
                // Property value was "[COSECFOLDER]rewrite_amd64_en-US.msi"
                // so session.CustomActionData.ToString() gives the resolved path
                string msiPath = session.CustomActionData.ToString();

                session.Log("URL Rewrite MSI path: " + msiPath);

                if (!System.IO.File.Exists(msiPath))
                {
                    session.Log("ERROR: URL Rewrite MSI not found at: " + msiPath);
                    return ActionResult.Failure;
                }

                session.Log("Creating background installer script...");

                string tempDir = System.IO.Path.GetTempPath();
                string scriptPath = System.IO.Path.Combine(tempDir, "install_urlrewrite.ps1");
                string logPath = System.IO.Path.Combine(tempDir, "urlrewrite_install.log");

                // Write PowerShell script that retries msiexec in a loop until the main installer unlocks msiexec
                string scriptContent = string.Format(
                    "$msiPath = \"{0}\"\r\n" +
                    "$logPath = \"{1}\"\r\n" +
                    "for ($i = 0; $i -lt 30; $i++) {{\r\n" +
                    "    $proc = Start-Process msiexec.exe -ArgumentList \"/i `\"$msiPath`\" /quiet /norestart /log `\"$logPath`\"\" -PassThru -Wait\r\n" +
                    "    if ($proc.ExitCode -ne 1618) {{\r\n" +
                    "        break\r\n" +
                    "    }}\r\n" +
                    "    Start-Sleep -Seconds 2\r\n" +
                    "}}\r\n" +
                    "Remove-Item $MyInvocation.MyCommand.Path -Force\r\n",
                    msiPath.Replace("\\", "\\\\"),
                    logPath.Replace("\\", "\\\\")
                );

                System.IO.File.WriteAllText(scriptPath, scriptContent, Encoding.UTF8);
                session.Log("Launching background installer script: " + scriptPath);

                var proc = new System.Diagnostics.Process
                {
                    StartInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName         = "powershell.exe",
                        Arguments        = string.Format(
                            "-NoProfile -ExecutionPolicy Bypass -File \"{0}\"",
                            scriptPath),
                        UseShellExecute  = false,
                        CreateNoWindow   = true
                    }
                };

                proc.Start();
                session.Log("Background installer started. Proceeding with main installation.");
                return ActionResult.Success;
            }
            catch (Exception ex)
            {
                session.Log("InstallUrlRewrite exception: " + ex.Message);
                session.Log(ex.StackTrace);
                return ActionResult.Failure;
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