// using MatPoll.Data;
// using MatPoll.Models;
// using Microsoft.EntityFrameworkCore;
// using Newtonsoft.Json.Linq;
// using Serilog;
// using System.Net;
// using System.Text;
// using System.Net.Http.Headers;
// namespace MatPoll.Services;

// public class GenetecSyncService : BackgroundService
// {
//     private readonly IServiceScopeFactory _scope;
//     private readonly IConfiguration       _config;

//     private static readonly Serilog.ILogger _info  = Log.ForContext("Sink", "info");
//     private static readonly Serilog.ILogger _debug = Log.ForContext("Sink", "debug");
//     private static readonly Serilog.ILogger _error = Log.ForContext("Sink", "error");

//     private string _baseUrl   = "";
//     private string _username  = "";
//     private string _password  = "";
//     private string _appId     = "";
//     private bool   _isEnabled = false;

//     public bool IsEnabled => _isEnabled;

//     public GenetecSyncService(
//         IServiceScopeFactory scope,
//         IConfiguration       config)
//     {
//         _scope  = scope;
//         _config = config;
//         BuildSettings();
//     }

//     private void BuildSettings()
//     {
//         var g      = _config.GetSection("Genetec");
//         var server = g["Server"];

//         if (string.IsNullOrEmpty(server))
//         {
//             _isEnabled = false;
//             _error.Warning("[GENETEC-SYNC] Genetec:Server not configured — disabled");
//             return;
//         }

//         var port   = g["Port"]    ?? "4590";
//         var uri    = g["BaseUri"] ?? "WebSdk";
//         var scheme = g.GetValue<bool>("UseHttps") ? "https" : "http";

//         _baseUrl   = $"{scheme}://{server}:{port}/{uri}/";
//         _username  = g["Username"]      ?? "";
//         _password  = g["Password"]      ?? "";
//         _appId     = g["ApplicationId"] ?? "";
//         _isEnabled = true;

//         _info.Information(
//             "[GENETEC-SYNC] Configured → URL:{Url}  User:{User}",
//             _baseUrl, _username);
//     }

//     protected override async Task ExecuteAsync(CancellationToken ct)
//     {
//         if (!_isEnabled)
//         {
//             _info.Warning(
//                 "[GENETEC-SYNC] Service disabled — " +
//                 "add Genetec:Server to appsettings.json");
//             return;
//         }

//         var intervalSec = _config.GetValue<int>(
//             "Genetec:SyncIntervalseconds", 60);

//         _info.Information(
//             "[GENETEC-SYNC] Backup polling started — every {Sec}s",
//             intervalSec);

//         while (!ct.IsCancellationRequested)
//         {
//             await Task.Delay(TimeSpan.FromSeconds(intervalSec), ct);
//             await RunSyncNowAsync(ct);
//         }
//     }

//     public async Task RunSyncNowAsync(CancellationToken ct)
//     {
//         if (!_isEnabled) return;

//         var start = DateTime.UtcNow;

//         _info.Information(
//             "[GENETEC-SYNC] Starting sync at {Time}",
//             start.ToString("HH:mm:ss"));

//         try
//         {
//             var cardholders = await FetchAllCardholdersAsync(ct);

//             _debug.Information(
//                 "[GENETEC-SYNC] Fetched {Count} cardholders",
//                 cardholders.Count);

//             int totalInserted = 0;
//             foreach (var ch in cardholders)
//             {
//                 var inserted = await InsertCommTrnForAllDevicesAsync(
//                     ch.Guid, ch.FirstName, ch.LastName, ct);
//                 totalInserted += inserted;
//             }

//             var duration = Math.Round(
//                 (DateTime.UtcNow - start).TotalSeconds, 1);

//             _info.Information(
//                 "[GENETEC-SYNC] Done — " +
//                 "Cardholders:{Total}  NewRows:{Inserted}  Duration:{Dur}s",
//                 cardholders.Count, totalInserted, duration);
//         }
//         catch (Exception ex)
//         {
//             _error.Error(ex,
//                 "[GENETEC-SYNC] Sync failed — {Msg}", ex.Message);
//         }
//     }

//     private async Task<List<(string Guid, string FirstName, string LastName)>>
//         FetchAllCardholdersAsync(CancellationToken ct)
//     {
//         //calling backend api : FetchCardholdersAsync
//         var url  = _baseUrl +
//             "report/CardholderConfiguration?q=Page=1,PageSize=1000";
//         var json = await SendAsync(url, "GET", ct);
//         var rsp  = ParseRsp(json);

//         var result = rsp["Result"] as JArray;
//         if (result == null)
//             return new List<(string, string, string)>();

//         //extract all guid from the result
//         var guids = result
//             .Select(item => (string?)item["Guid"])
//             .Where(g => !string.IsNullOrWhiteSpace(g))
//             .Distinct()
//             .ToList();

//         var cardholders =
//             new List<(string Guid, string FirstName, string LastName)>();

//         foreach (var guid in guids)
//         {
//             try
//             {
//                 //api hit for each guid to get details
//                 var detailUrl  = _baseUrl +
//                     "entity?q=entity=" + guid!.Replace("-", "") +
//                     ",FirstName,LastName";
//                 var detailJson = await SendAsync(detailUrl, "GET", ct);
//                 var detailRsp  = ParseRsp(detailJson);
//                 var obj        = detailRsp["Result"] as JObject;

//                 cardholders.Add((
//                     guid!,
//                     (string?)obj?["FirstName"] ?? "",
//                     (string?)obj?["LastName"]  ?? ""
//                 ));
//             }
//             catch (Exception ex)
//             {
//                 _error.Warning(
//                     "[GENETEC-SYNC] Detail fetch failed {Guid}: {Msg}",
//                     guid, ex.Message);
//             }
//         }

//         return cardholders;
//     }

//     public async Task<int> InsertCommTrnForAllDevicesAsync(
//         string guid,
//         string firstName,
//         string lastName,
//         CancellationToken ct)
//     {
//         if (string.IsNullOrEmpty(guid)) return 0;

//         try
//         {
//             using var scope = _scope.CreateScope();
//             var db = scope.ServiceProvider
//                 .GetRequiredService<AppDbContext>();

//             // Get all active devices
//             var devices = await db.Devices
//                 .AsNoTracking()
//                 .Where(d => d.IsActive == 1)
//                 .Select(d => new { d.DeviceID, d.DeviceType })
//                 .ToListAsync(ct);

//             if (devices.Count == 0) return 0;

//             var existingList = await db.CommTrns
//                 .AsNoTracking()
//                 .Where(t =>
//                     t.MsgStr!.Contains($"UID:{guid}") &&
//                     t.TrnStat != 99)
//                 .Select(t => t.DeviceID)
//                 .ToListAsync(ct);

//             var existingSet = existingList.ToHashSet();

//             var now     = DateTime.UtcNow;
//             var newRows = devices
//                 .Where(d => !existingSet.Contains(d.DeviceID))
//                 .Select(d => new MatCommTrn
//                 {
//                     MsgStr     = $"ENROLL|UID:{guid}|NAME:{firstName} {lastName}|DID:{(int)d.DeviceID}",
//                     RetryCnt   = 0,
//                     TrnStat    = 0,
//                     CreatedAt  = now,
//                     DeviceID   = d.DeviceID,
//                     DeviceType = d.DeviceType ?? 0
//                 })
//                 .ToList();

//             if (newRows.Count == 0)
//             {
//                 _debug.Information(
//                     "[GENETEC] Already enrolled on all devices — {Guid}", guid);
//                 return 0;
//             }

//             db.CommTrns.AddRange(newRows);
//             await db.SaveChangesAsync(ct);

//             _info.Information(
//                 "[GENETEC] Inserted {Count} rows — Name:{Name}  Guid:{Guid}",
//                 newRows.Count,
//                 $"{firstName} {lastName}",
//                 guid);

//             return newRows.Count;
//         }
//         catch (Exception ex)
//         {
//             _error.Error(ex,
//                 "[GENETEC] InsertCommTrn failed — {Msg}", ex.Message);
//             return 0;
//         }
//     }

//     // Uses NetworkCredential exactly like working WPF app
//    private async Task<string> SendAsync(
//     string url, string method, CancellationToken ct)
// {
//     var handler = new HttpClientHandler
//     {
//         AllowAutoRedirect        = true,
//         MaxAutomaticRedirections = 10
    
//     };

//     using var client = new HttpClient(handler);
//     client.Timeout = TimeSpan.FromSeconds(15);

//     // ── Explicit Basic Auth header — same as working WPF app ──
//     var encoded = Convert.ToBase64String(
//         Encoding.ASCII.GetBytes($"{_username};{_appId}:{_password}"));

//     client.DefaultRequestHeaders.Authorization =
//         new AuthenticationHeaderValue("Basic", encoded);

//     client.DefaultRequestHeaders.Add("Accept", "text/json");

//     HttpResponseMessage response;
//     if (method == "GET")
//         response = await client.GetAsync(url, ct);
//     else
//         response = await client.PostAsync(url, null, ct);

//     var body = await response.Content.ReadAsStringAsync(ct);

//     if (!response.IsSuccessStatusCode)
//         throw new Exception($"HTTP {(int)response.StatusCode}: {body}");

//     return body.Trim();
// }

//     private static JObject ParseRsp(string json)
//     {
//         var root = JObject.Parse(json);
//         var rsp  = root["Rsp"] as JObject
//             ?? throw new Exception($"Missing Rsp: {json}");
//         var status = (string?)rsp["Status"];
//         if (!string.Equals(status, "Ok", StringComparison.OrdinalIgnoreCase))
//             throw new Exception($"Genetec error: {status}\n{json}");
//         return rsp;
//     }
// }



using MatPoll.Data;
using MatPoll.Models;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json.Linq;
using Serilog;
using System.Net.Http.Headers;
using System.Text;

namespace MatPoll.Services;

// ─────────────────────────────────────────────────────────────────────────────
// GenetecSyncService — BACKUP polling
// Single HttpClient reused for all calls (one socket pool)
// Secure redirect handling — only follows to same host
// ─────────────────────────────────────────────────────────────────────────────

public class GenetecSyncService : BackgroundService
{
    // ── Fields ────────────────────────────────────────────────────────────────
    private readonly IServiceScopeFactory _scope;
    private readonly IConfiguration       _config;

    private static readonly Serilog.ILogger _info  = Log.ForContext("Sink", "info");
    private static readonly Serilog.ILogger _debug = Log.ForContext("Sink", "debug");
    private static readonly Serilog.ILogger _error = Log.ForContext("Sink", "error");

    private string     _baseUrl     = "";
    private string     _username    = "";
    private string     _password    = "";
    private string     _appId       = "";
    private string     _allowedHost = ""; // ← for redirect security check
    private bool       _isEnabled   = false;
    private HttpClient _client      = null!; // ← ONE client reused forever

    public bool IsEnabled => _isEnabled;

    // ── Constructor ───────────────────────────────────────────────────────────
    public GenetecSyncService(
        IServiceScopeFactory scope,
        IConfiguration config)
    {
        _scope  = scope;
        _config = config;
        BuildSettings();
    }

    // ── Build settings + single HttpClient ───────────────────────────────────
    private void BuildSettings()
    {
        var g      = _config.GetSection("Genetec");
        var server = g["Server"];

        if (string.IsNullOrEmpty(server))
        {
            _isEnabled = false;
            _error.Warning(
                "[GENETEC-SYNC] Genetec:Server not configured — disabled");
            return;
        }

        var port   = g["Port"]    ?? "4590";
        var uri    = g["BaseUri"] ?? "WebSdk";
        var scheme = g.GetValue<bool>("UseHttps") ? "https" : "http";

        _baseUrl     = $"{scheme}://{server}:{port}/{uri}/";
        _allowedHost = server; // ← only redirect to this host allowed
        _username    = g["Username"]      ?? "";
        _password    = g["Password"]      ?? "";
        _appId       = g["ApplicationId"] ?? "";
        _isEnabled   = true;

        // ── Build ONE HttpClient — lives for entire service lifetime ──────────
        // AllowAutoRedirect = false → we handle redirects manually for security
        // No Credentials in handler → we use Authorization header instead
        // This prevents credentials leaking to redirected servers
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false, // ← manual redirect = secure ✅
        };

        _client         = new HttpClient(handler);
        _client.Timeout = TimeSpan.FromSeconds(15);

        // Authorization header — NOT forwarded on redirect ✅
        // Credentials in handler ARE forwarded on redirect ❌ (security risk)
        var raw     = $"{_username};{_appId}:{_password}";
        var encoded = Convert.ToBase64String(
            Encoding.ASCII.GetBytes(raw));

        _client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Basic", encoded);
        _client.DefaultRequestHeaders.Add("Accept", "text/json");

        _info.Information(
            "[GENETEC-SYNC] Configured → URL:{Url}  User:{User}",
            _baseUrl, _username);
    }

    // ── Background service entry ──────────────────────────────────────────────
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (!_isEnabled)
        {
            _info.Warning(
                "[GENETEC-SYNC] Service disabled — " +
                "add Genetec:Server to appsettings.json");
            return;
        }

        var intervalSec = _config.GetValue<int>(
            "Genetec:SyncIntervalSeconds", 60);

        _info.Information(
            "[GENETEC-SYNC] Backup polling started — every {Sec}s",
            intervalSec);

        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(
                TimeSpan.FromSeconds(intervalSec), ct);

            await RunSyncNowAsync(ct);
        }
    }

    // ── Public: called by StreamService on disconnect ─────────────────────────
    public async Task RunSyncNowAsync(CancellationToken ct)
    {
        if (!_isEnabled) return;

        var start = DateTime.UtcNow;

        _info.Information(
            "[GENETEC-SYNC] Starting sync at {Time}",
            start.ToString("HH:mm:ss"));

        try
        {
            var cardholders = await FetchAllCardholdersAsync(ct);

            _debug.Information(
                "[GENETEC-SYNC] Fetched {Count} cardholders",
                cardholders.Count);

            int totalInserted = 0;
            foreach (var ch in cardholders)
            {
                var inserted = await InsertCommTrnForAllDevicesAsync(
                    ch.Guid, ch.FirstName, ch.LastName, ct);
                totalInserted += inserted;
            }

            var duration = Math.Round(
                (DateTime.UtcNow - start).TotalSeconds, 1);

            _info.Information(
                "[GENETEC-SYNC] ✅ Done — " +
                "Cardholders:{Total}  NewRows:{Inserted}  Duration:{Dur}s",
                cardholders.Count, totalInserted, duration);
        }
        catch (Exception ex)
        {
            _error.Error(ex,
                "[GENETEC-SYNC] Sync failed — {Msg}", ex.Message);
        }
    }

    // ── Fetch all cardholders from Genetec ────────────────────────────────────
    private async Task<List<(string Guid, string FirstName, string LastName)>>
        FetchAllCardholdersAsync(CancellationToken ct)
    {
        var url  = _baseUrl +
            "report/CardholderConfiguration?q=Page=1,PageSize=1000";
        var json = await SendAsync(url, HttpMethod.Get, ct);
        var rsp  = ParseRsp(json);

        var result = rsp["Result"] as JArray;
        if (result == null)
            return new List<(string, string, string)>();

        // Extract all GUIDs from list
        var guids = result
            .Select(item => (string?)item["Guid"])
            .Where(g => !string.IsNullOrWhiteSpace(g))
            .Distinct()
            .ToList();

        _debug.Information(
            "[GENETEC-SYNC] Found {Count} cardholder GUIDs",
            guids.Count);

        var cardholders =
            new List<(string Guid, string FirstName, string LastName)>();

        // Fetch details for each GUID — reuses same socket ✅
        foreach (var guid in guids)
        {
            try
            {
                var detailUrl  = _baseUrl +
                    "entity?q=entity=" + guid!.Replace("-", "") +
                    ",FirstName,LastName";

                var detailJson = await SendAsync(detailUrl, HttpMethod.Get, ct);
                var detailRsp  = ParseRsp(detailJson);
                var obj        = detailRsp["Result"] as JObject;

                cardholders.Add((
                    guid!,
                    (string?)obj?["FirstName"] ?? "",
                    (string?)obj?["LastName"]  ?? ""
                ));
            }
            catch (Exception ex)
            {
                _error.Warning(
                    "[GENETEC-SYNC] Detail fetch failed {Guid}: {Msg}",
                    guid, ex.Message);
            }
        }

        return cardholders;
    }

    // ── Insert CommTrn rows for all active devices ────────────────────────────
    public async Task<int> InsertCommTrnForAllDevicesAsync(
        string guid,
        string firstName,
        string lastName,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(guid)) return 0;

        try
        {
            using var scope = _scope.CreateScope();
            var db = scope.ServiceProvider
                .GetRequiredService<AppDbContext>();

            // Get all active devices
            var devices = await db.Devices
                .AsNoTracking()
                .Where(d => d.IsActive == 1)
                .Select(d => new { d.DeviceID, d.DeviceType })
                .ToListAsync(ct);

            if (devices.Count == 0)
            {
                _error.Warning("[GENETEC] No active devices found");
                return 0;
            }

            // Duplicate check — which devices already have this cardholder
            var existingList = await db.CommTrns
                .AsNoTracking()
                .Where(t =>
                    t.MsgStr!.Contains($"UID:{guid}") &&
                    t.TrnStat != 99)
                .Select(t => t.DeviceID)
                .ToListAsync(ct);

            var existingSet = existingList.ToHashSet();

            // Build rows only for devices missing this cardholder
            var now     = DateTime.UtcNow;
            var newRows = devices
                .Where(d => !existingSet.Contains(d.DeviceID))
                .Select(d => new MatCommTrn
                {
                    MsgStr     = $"ENROLL|UID:{guid}|NAME:{firstName} {lastName}|DID:{(int)d.DeviceID}",
                    RetryCnt   = 0,
                    TrnStat    = 0,
                    CreatedAt  = now,
                    DeviceID   = d.DeviceID,
                    DeviceType = d.DeviceType ?? 0
                })
                .ToList();

            if (newRows.Count == 0)
            {
                _debug.Information(
                    "[GENETEC] Already enrolled on all devices — {Guid}", guid);
                return 0;
            }

            // ONE SaveChanges = ONE DB round trip for all rows ✅
            db.CommTrns.AddRange(newRows);
            await db.SaveChangesAsync(ct);

            _info.Information(
                "[GENETEC] ✅ Inserted {Count} rows — " +
                "Name:{Name}  Guid:{Guid}",
                newRows.Count,
                $"{firstName} {lastName}",
                guid);

            return newRows.Count;
        }
        catch (Exception ex)
        {
            _error.Error(ex,
                "[GENETEC] InsertCommTrn failed — {Msg}", ex.Message);
            return 0;
        }
    }

    // ── Secure HTTP sender — reuses ONE socket, handles redirects manually ────
    // ONE _client = ONE socket pool = much faster than new client per call
    // Manual redirect = credentials never sent to wrong server
    private async Task<string> SendAsync(
        string url, HttpMethod method, CancellationToken ct)
    {
        const int maxRedirects = 3;
        var currentUrl = url;

        for (int i = 0; i <= maxRedirects; i++)
        {
            var request  = new HttpRequestMessage(method, currentUrl);
            var response = await _client.SendAsync(request, ct);

            // ── Handle redirect ───────────────────────────────────────────────
            if (response.StatusCode == System.Net.HttpStatusCode.MovedPermanently  ||
                response.StatusCode == System.Net.HttpStatusCode.Found             ||
                response.StatusCode == System.Net.HttpStatusCode.TemporaryRedirect ||
                response.StatusCode == System.Net.HttpStatusCode.PermanentRedirect)
            {
                var location = response.Headers.Location;

                if (location == null)
                    throw new Exception("Redirect with no Location header");

                // ── SECURITY CHECK 1: Only redirect to same host ──────────────
                // Prevents open redirect attack → credentials leak to attacker
                if (!string.IsNullOrEmpty(location.Host) &&
                    location.Host != _allowedHost)
                {
                    _error.Warning(
                        "[GENETEC-SYNC] Redirect to different host BLOCKED — " +
                        "AllowedHost:{Allowed}  RedirectHost:{Redir}",
                        _allowedHost, location.Host);

                    throw new Exception(
                        $"Redirect to untrusted host blocked: {location.Host}");
                }

                // ── SECURITY CHECK 2: Block HTTPS → HTTP downgrade ────────────
                // Prevents credentials being sent in plain text
                var originalScheme = new Uri(currentUrl).Scheme;
                if (originalScheme == "https" &&
                    location.Scheme == "http")
                {
                    _error.Warning(
                        "[GENETEC-SYNC] HTTPS→HTTP downgrade BLOCKED: {Url}",
                        location);

                    throw new Exception(
                        "HTTPS to HTTP downgrade blocked for security");
                }

                _debug.Information(
                    "[GENETEC-SYNC] Following redirect {N}/{Max}: {Url}",
                    i + 1, maxRedirects, location);

                currentUrl = location.ToString();
                continue; // follow redirect
            }

            // ── Not a redirect — return body ──────────────────────────────────
            var body = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
                throw new Exception(
                    $"HTTP {(int)response.StatusCode}: {body}");

            return body.Trim();
        }

        throw new Exception($"Too many redirects — max {maxRedirects}");
    }

    // ── Parse Genetec response ────────────────────────────────────────────────
    private static JObject ParseRsp(string json)
    {
        var root = JObject.Parse(json);
        var rsp  = root["Rsp"] as JObject
            ?? throw new Exception($"Missing Rsp: {json}");

        var status = (string?)rsp["Status"];
        if (!string.Equals(status, "Ok", StringComparison.OrdinalIgnoreCase))
            throw new Exception($"Genetec error: {status}\n{json}");

        return rsp;
    }
}