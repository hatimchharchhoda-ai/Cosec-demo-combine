using MatPoll.Data;
using MatPoll.Models;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json.Linq;
using Serilog;
using System.Net;
using System.Text;
using System.Net.Http.Headers;
namespace MatPoll.Services;

public class GenetecSyncService : BackgroundService
{
    private readonly IServiceScopeFactory _scope;
    private readonly IConfiguration       _config;

    private static readonly Serilog.ILogger _info  = Log.ForContext("Sink", "info");
    private static readonly Serilog.ILogger _debug = Log.ForContext("Sink", "debug");
    private static readonly Serilog.ILogger _error = Log.ForContext("Sink", "error");

    private string _baseUrl   = "";
    private string _username  = "";
    private string _password  = "";
    private string _appId     = "";
    private bool   _isEnabled = false;

    public bool IsEnabled => _isEnabled;

    public GenetecSyncService(
        IServiceScopeFactory scope,
        IConfiguration       config)
    {
        _scope  = scope;
        _config = config;
        BuildSettings();
    }

    private void BuildSettings()
    {
        var g      = _config.GetSection("Genetec");
        var server = g["Server"];

        if (string.IsNullOrEmpty(server))
        {
            _isEnabled = false;
            _error.Warning("[GENETEC-SYNC] Genetec:Server not configured — disabled");
            return;
        }

        var port   = g["Port"]    ?? "4590";
        var uri    = g["BaseUri"] ?? "WebSdk";
        var scheme = g.GetValue<bool>("UseHttps") ? "https" : "http";

        _baseUrl   = $"{scheme}://{server}:{port}/{uri}/";
        _username  = g["Username"]      ?? "";
        _password  = g["Password"]      ?? "";
        _appId     = g["ApplicationId"] ?? "";
        _isEnabled = true;

        _info.Information(
            "[GENETEC-SYNC] Configured → URL:{Url}  User:{User}",
            _baseUrl, _username);
    }

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
            "Genetec:SyncIntervalseconds", 60);

        _info.Information(
            "[GENETEC-SYNC] Backup polling started — every {Sec}s",
            intervalSec);

        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(intervalSec), ct);
            await RunSyncNowAsync(ct);
        }
    }

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
                "[GENETEC-SYNC] Done — " +
                "Cardholders:{Total}  NewRows:{Inserted}  Duration:{Dur}s",
                cardholders.Count, totalInserted, duration);
        }
        catch (Exception ex)
        {
            _error.Error(ex,
                "[GENETEC-SYNC] Sync failed — {Msg}", ex.Message);
        }
    }

    private async Task<List<(string Guid, string FirstName, string LastName)>>
        FetchAllCardholdersAsync(CancellationToken ct)
    {
        //calling backend api : FetchCardholdersAsync
        var url  = _baseUrl +
            "report/CardholderConfiguration?q=Page=1,PageSize=1000";
        var json = await SendAsync(url, "GET", ct);
        var rsp  = ParseRsp(json);

        var result = rsp["Result"] as JArray;
        if (result == null)
            return new List<(string, string, string)>();

        //extract all guid from the result
        var guids = result
            .Select(item => (string?)item["Guid"])
            .Where(g => !string.IsNullOrWhiteSpace(g))
            .Distinct()
            .ToList();

        var cardholders =
            new List<(string Guid, string FirstName, string LastName)>();

        foreach (var guid in guids)
        {
            try
            {
                //api hit for each guid to get details
                var detailUrl  = _baseUrl +
                    "entity?q=entity=" + guid!.Replace("-", "") +
                    ",FirstName,LastName";
                var detailJson = await SendAsync(detailUrl, "GET", ct);
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

            if (devices.Count == 0) return 0;

            var existingList = await db.CommTrns
                .AsNoTracking()
                .Where(t =>
                    t.MsgStr!.Contains($"UID:{guid}") &&
                    t.TrnStat != 99)
                .Select(t => t.DeviceID)
                .ToListAsync(ct);

            var existingSet = existingList.ToHashSet();

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

            db.CommTrns.AddRange(newRows);
            await db.SaveChangesAsync(ct);

            _info.Information(
                "[GENETEC] Inserted {Count} rows — Name:{Name}  Guid:{Guid}",
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

    // Uses NetworkCredential exactly like working WPF app
   private async Task<string> SendAsync(
    string url, string method, CancellationToken ct)
{
    var handler = new HttpClientHandler
    {
        AllowAutoRedirect        = true,
        MaxAutomaticRedirections = 10
    
    };

    using var client = new HttpClient(handler);
    client.Timeout = TimeSpan.FromSeconds(15);

    // ── Explicit Basic Auth header — same as working WPF app ──
    var encoded = Convert.ToBase64String(
        Encoding.ASCII.GetBytes($"{_username};{_appId}:{_password}"));

    client.DefaultRequestHeaders.Authorization =
        new AuthenticationHeaderValue("Basic", encoded);

    client.DefaultRequestHeaders.Add("Accept", "text/json");

    HttpResponseMessage response;
    if (method == "GET")
        response = await client.GetAsync(url, ct);
    else
        response = await client.PostAsync(url, null, ct);

    var body = await response.Content.ReadAsStringAsync(ct);

    if (!response.IsSuccessStatusCode)
        throw new Exception($"HTTP {(int)response.StatusCode}: {body}");

    return body.Trim();
}

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