using System;
using System.IO;
using System.Text;
using Microsoft.Deployment.WindowsInstaller;

namespace CustomActions
{
    public class CustomActions
    {
        /// <summary>
        /// Called as a deferred custom action during install / reinstall.
        /// Reads all DB / JWT properties (passed via CustomActionData) and
        /// writes a fresh appsettings.json into the COSEC installation folder.
        /// </summary>
        [CustomAction]
        public static ActionResult WriteAppSettings(Session session)
        {
            try
            {
                // When a custom action is deferred, properties are passed through
                // the special CustomActionData property as key=value pairs.
                var data = new CustomActionData(session["CustomActionData"]);

                string installDir  = data["COSECFOLDER"];
                string dbServer    = data["DB_SERVER"];
                string dbName      = data["DB_NAME"];
                string dbUser      = data["DB_USER"];
                string dbPassword  = data["DB_PASSWORD"];

                // Build connection string
                string connString =
                    $"Server={dbServer};Database={dbName};User Id={dbUser};" +
                    $"Password={dbPassword};TrustServerCertificate=True;" +
                    $"MultipleActiveResultSets=true";

                // Build JSON manually to avoid a Newtonsoft dependency in the CA DLL.
                string json = $@"{{
                ""Logging"": {{
                    ""LogLevel"": {{
                    ""Default"": ""Information"",
                    ""Microsoft.AspNetCore"": ""Warning""
                    }}
                }},
                ""AllowedHosts"": ""*"",
                ""ConnectionStrings"": {{
                    ""DefaultConnection"": ""{EscapeJson(connString)}""
                }},
                ""JwtSettings"": {{
                    ""SecretKey"": ""YOUR_SECRET_KEY_MIN_32_CHARS_LONG"",
                    ""Issuer"": ""COSEC_API"",
                    ""Audience"": ""COSEC_CLIENT"",
                    ""ExpiryMinutes"": 60
                }}
                }}
                ";
                string targetPath = Path.Combine(installDir, "appsettings.json");
                File.WriteAllText(targetPath, json, Encoding.UTF8);

                session.Log($"[WriteAppSettings] Written to: {targetPath}");
                return ActionResult.Success;
            }
            catch (Exception ex)
            {
                session.Log($"[WriteAppSettings] ERROR: {ex}");
                return ActionResult.Failure;
            }
        }

        /// Called as a deferred custom action during uninstall to remove the
        /// generated appsettings.json (optional – WiX cleans up installed files
        /// already; this handles the generated-at-runtime copy).
        [CustomAction]
        public static ActionResult RemoveAppSettings(Session session)
        {
            try
            {
                var data = new CustomActionData(session["CustomActionData"]);
                string installDir = data["COSECFOLDER"];
                string targetPath = Path.Combine(installDir, "appsettings.json");

                if (File.Exists(targetPath))
                    File.Delete(targetPath);

                return ActionResult.Success;
            }
            catch (Exception ex)
            {
                session.Log($"[RemoveAppSettings] ERROR: {ex}");
                return ActionResult.Success; // non-fatal on uninstall
            }
        }

        // ------------------------------------------------------------------ //

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