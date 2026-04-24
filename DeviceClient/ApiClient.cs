using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

public class ApiClient
{
    private readonly HttpClient  _http;
    private readonly DeviceInfo  _device;
    private readonly DeviceLogger _logger;      // ← per-device, injected
    private readonly string      _label;

    // Per-instance state
    private bool          _isConnected = false;
    private string?       _token;
    private string?       _typeMid;
    private int?          _deviceId;
    private List<decimal> _lastIds = new();
    private int           _refreshFailCount = 0;

    // Retry / back-off state
    private int _loginFailCount = 0;
    private static readonly int[] LoginBackoffSeconds = { 5, 10, 20, 30, 60 };

    public ApiClient(string baseUrl, DeviceInfo device, DeviceLogger logger)
    {
        _device = device;
        _logger = logger;
        _label  = $"[{device.MACAddr}]";

        if (string.IsNullOrWhiteSpace(baseUrl))
            _logger.Error($"{_label} INIT | BaseUrl is null or empty — HttpClient will not work correctly");

        var handler = new SocketsHttpHandler
        {
            KeepAlivePingDelay             = TimeSpan.FromSeconds(10),
            KeepAlivePingTimeout           = TimeSpan.FromSeconds(5),
            EnableMultipleHttp2Connections = true,
            PooledConnectionLifetime       = TimeSpan.FromMinutes(5),
            PooledConnectionIdleTimeout    = TimeSpan.FromMinutes(1),
            ConnectTimeout                 = TimeSpan.FromSeconds(5),
        };

        _http = new HttpClient(handler)
        {
            BaseAddress           = new Uri(baseUrl),
            Timeout               = TimeSpan.FromSeconds(15),
            DefaultRequestVersion = new Version(2, 0),
            DefaultVersionPolicy  = HttpVersionPolicy.RequestVersionOrLower,
        };

        _logger.Debug($"{_label} INIT | BaseUrl={baseUrl} DeviceType={device.DeviceType} IP={device.IPAddr}");
    }

    // ── Public surface ─────────────────────────────────────────────────────────

    public bool IsConnected => _isConnected;

    public async Task Login()
    {
        var ctx = $"{_label} LOGIN";
        _logger.Info($"{ctx} | START | Attempt={_loginFailCount + 1}");

        if (string.IsNullOrWhiteSpace(_device.MACAddr))
        {
            _logger.Missing(ctx, "MACAddr", "Device config is missing MAC address — login aborted");
            _isConnected = false;
            return;
        }
        if (string.IsNullOrWhiteSpace(_device.IPAddr))
            _logger.Warn($"{ctx} | IPAddr is blank — proceeding but server may reject");

        var payload = new
        {
            DeviceType = _device.DeviceType,
            MACAddr    = _device.MACAddr,
            IPAddr     = _device.IPAddr,
            T1         = DateTime.UtcNow
        };

        var req = new HttpRequestMessage(HttpMethod.Post, "auth/login")
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };

        try
        {
            var (res, body) = await HttpLogger.SendAsync(_http, req, ctx, _logger);

            if (!res.IsSuccessStatusCode)
            {
                _loginFailCount++;
                _logger.Error($"{ctx} | FAILED | Status={(int)res.StatusCode} | Body={body} | FailCount={_loginFailCount}");
                _isConnected = false;
                return;
            }

            JsonElement doc;
            try { doc = JsonDocument.Parse(body).RootElement; }
            catch (JsonException jex)
            {
                _logger.Error($"{ctx} | PARSE-ERROR | {jex.Message} | Body={body}");
                _isConnected = false;
                return;
            }

            if (!doc.TryGetProperty("token", out var tokenEl) || string.IsNullOrWhiteSpace(tokenEl.GetString()))
            {
                _logger.Missing(ctx, "token", "Login response missing 'token' field");
                _isConnected = false;
                return;
            }

            if (!doc.TryGetProperty("typeMID",   out var typeMidEl))  _logger.Missing(ctx, "typeMID",  "Login response missing 'typeMID'");
            if (!doc.TryGetProperty("deviceId",  out var deviceIdEl)) _logger.Missing(ctx, "deviceId", "Login response missing 'deviceId'");

            var newToken    = tokenEl.GetString();
            var newTypeMid  = typeMidEl.ValueKind  != JsonValueKind.Undefined ? typeMidEl.GetString()   : null;
            var newDeviceId = deviceIdEl.ValueKind != JsonValueKind.Undefined ? (int?)deviceIdEl.GetInt32() : null;

            if (_token    != null && _token    != newToken)    _logger.Warn($"{ctx} | TOKEN CHANGED mid-session");
            if (_typeMid  != null && _typeMid  != newTypeMid)  _logger.Mismatch(ctx, "typeMID",  _typeMid,  newTypeMid);
            if (_deviceId != null && _deviceId != newDeviceId) _logger.Mismatch(ctx, "deviceId", _deviceId, newDeviceId, isError: true);

            _token    = newToken;
            _typeMid  = newTypeMid;
            _deviceId = newDeviceId;

            _isConnected    = true;
            _loginFailCount = 0;

            _logger.Info($"{ctx} | SUCCESS | DeviceID={_deviceId} TypeMID={_typeMid}");

            _ = Task.Run(Restore);
            _ = Task.Run(TokenRefreshLoop);
        }
        catch (TaskCanceledException tcex)
        {
            _loginFailCount++;
            _logger.Error($"{ctx} | TIMEOUT | {tcex.Message} | FailCount={_loginFailCount}");
            _isConnected = false;
        }
        catch (HttpRequestException hre)
        {
            _logger.Error($"{ctx} | HTTP-ERROR | {hre.Message}");
            if (hre.InnerException is System.Net.Sockets.SocketException ||
                hre.Message.Contains("refused") || hre.Message.Contains("No connection"))
            {
                _logger.Warn($"{ctx} | SERVER-DOWN detected — marking disconnected");
                _isConnected = false;
            }
        }
        catch (Exception ex)
        {
            _loginFailCount++;
            _logger.Error($"{ctx} | EXCEPTION | {ex.GetType().Name}: {ex.Message} | FailCount={_loginFailCount}");
            _isConnected = false;
        }
    }

    public async Task PollAndProcess()
    {
        var ctx = $"{_label} POLL";

        if (!_isConnected)
        {
            _logger.Debug($"{ctx} | Skipped — not connected");
            return;
        }

        try
        {
            HttpRequestMessage req;
            try { req = await CreateAuthedRequest(HttpMethod.Get, "poll"); }
            catch (InvalidOperationException ioe)
            {
                _logger.Error($"{ctx} | REQUEST-BUILD-FAILED | {ioe.Message} — marking disconnected");
                _isConnected = false;
                return;
            }

            var (res, body) = await HttpLogger.SendAsync(_http, req, ctx, _logger);

            if (res.StatusCode == HttpStatusCode.Unauthorized)
            {
                _logger.Warn($"{ctx} | UNAUTHORIZED | Marking disconnected for reconnect");
                _isConnected = false;
                return;
            }

            if (!res.IsSuccessStatusCode)
            {
                _logger.UnexpectedResponse(ctx, (int)res.StatusCode, body, "Non-success from poll endpoint");
                return;
            }

            if (string.IsNullOrWhiteSpace(body))
            {
                _logger.Warn($"{ctx} | EMPTY-BODY | Server returned success with no body");
                return;
            }

            PollResponse? poll;
            try
            {
                poll = JsonSerializer.Deserialize<PollResponse>(body,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (JsonException jex)
            {
                _logger.Error($"{ctx} | PARSE-ERROR | {jex.Message} | Body={body}");
                return;
            }

            if (poll == null)
            {
                _logger.Warn($"{ctx} | NULL-POLL | Deserialization returned null — Body={body}");
                return;
            }

            if (poll.TypeMID != null && _typeMid != null && poll.TypeMID != _typeMid)
                _logger.Mismatch(ctx, "TypeMID", _typeMid, poll.TypeMID, isError: true);

            if (poll.NeedAckFirst)
            {
                _logger.Info($"{ctx} | NEED-ACK-FIRST | PendingIds={_lastIds.Count}");
                if (_lastIds.Count > 0)
                {
                    await Ack(_lastIds);
                    _lastIds.Clear();
                }
                else
                {
                    _logger.Warn($"{ctx} | NEED-ACK-FIRST | _lastIds is empty — possible state desync");
                }
                return;
            }

            if (poll.HasData)
            {
                if (poll.Rows == null || poll.Rows.Count == 0)
                {
                    _logger.Warn($"{ctx} | HAS-DATA=true but Rows is empty or null — Body={body}");
                    return;
                }

                var ids = new List<decimal>();
                foreach (var row in poll.Rows)
                {
                    if (row.TrnID == 0)
                        _logger.Warn($"{ctx} | ROW-WARN | TrnID=0 (may be invalid) | MsgStr={row.MsgStr}");
                    if (string.IsNullOrWhiteSpace(row.MsgStr))
                        _logger.Warn($"{ctx} | ROW-WARN | TrnID={row.TrnID} has empty MsgStr");
                    if (row.RetryCnt > 0)
                        _logger.Warn($"{ctx} | ROW-RETRY | TrnID={row.TrnID} RetryCnt={row.RetryCnt}");
                    if (row.TypeMID != null && _typeMid != null && row.TypeMID != _typeMid)
                        _logger.Mismatch(ctx, $"Row.TypeMID [TrnID={row.TrnID}]", _typeMid, row.TypeMID);
                    ids.Add(row.TrnID);
                }

                _lastIds = ids;
                _logger.Info($"{ctx} | DATA | Count={ids.Count} TotalPending={poll.TotalPending} IDs={string.Join(",", ids)}");

                await Ack(ids);
                _lastIds.Clear();
            }
            else
            {
                _logger.Debug($"{ctx} | NO-DATA | TotalPending={poll.TotalPending}");
            }
        }
        catch (TaskCanceledException tcex) { _logger.Error($"{ctx} | TIMEOUT | {tcex.Message}"); }
        catch (HttpRequestException hre)   { _logger.Error($"{ctx} | HTTP-ERROR | {hre.Message}"); }
        catch (Exception ex)               { _logger.Error($"{ctx} | EXCEPTION | {ex.GetType().Name}: {ex.Message}"); }
    }

    public async Task SendEventAsync(string message, int counter)
    {
        var ctx = $"{_label} EVENT";

        if (!_isConnected)
        {
            _logger.Warn($"{ctx} | SKIPPED | Not connected");
            return;
        }

        var payload = new
        {
            TypeMID    = _typeMid,
            Message    = message,
            T1         = DateTime.UtcNow,
            EventSeqNo = counter
        };

        var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        HttpRequestMessage req;
        try { req = await CreateAuthedRequest(HttpMethod.Post, "poll/events", content); }
        catch (InvalidOperationException ioe)
        {
            _logger.Error($"{ctx} | REQUEST-BUILD-FAILED | {ioe.Message}");
            _isConnected = false;
            return;
        }

        try
        {
            var (res, body) = await HttpLogger.SendAsync(_http, req, ctx, _logger);

            if (res.StatusCode == HttpStatusCode.Unauthorized)
            {
                _logger.Warn($"{ctx} | UNAUTHORIZED — marking disconnected");
                _isConnected = false;
                return;
            }

            if (!res.IsSuccessStatusCode)
                _logger.UnexpectedResponse(ctx, (int)res.StatusCode, body, "Event rejected");
        }
        catch (Exception ex)
        {
            _logger.Error($"{ctx} | EXCEPTION | {ex.Message}");
            if (ex is HttpRequestException hre &&
                (hre.InnerException is System.Net.Sockets.SocketException || hre.Message.Contains("refused")))
            {
                _logger.Warn($"{ctx} | SERVER-DOWN detected in event — marking disconnected");
                _isConnected = false;
            }
        }
    }

    public async Task Restore()
    {
        var ctx = $"{_label} RESTORE";
        _logger.Info($"{ctx} | START");

        if (!_isConnected)
        {
            _logger.Warn($"{ctx} | SKIPPED | Not connected");
            return;
        }

        try
        {
            var req = await CreateAuthedRequest(HttpMethod.Post, "poll/restore");
            var (res, body) = await HttpLogger.SendAsync(_http, req, ctx, _logger);

            if (!res.IsSuccessStatusCode)
                _logger.UnexpectedResponse(ctx, (int)res.StatusCode, body, "Restore failed");
            else
                _logger.Info($"{ctx} | SUCCESS");
        }
        catch (Exception ex)
        {
            _logger.Error($"{ctx} | EXCEPTION | {ex.GetType().Name}: {ex.Message}");
        }
    }

    // ── Private helpers ────────────────────────────────────────────────────────

    private async Task Ack(List<decimal> ids)
    {
        var ctx = $"{_label} ACK";
        _logger.Debug($"{ctx} | START | IDs={string.Join(",", ids)}");

        if (ids == null || ids.Count == 0)
        {
            _logger.Warn($"{ctx} | SKIPPED | Empty IDs list");
            return;
        }

        try
        {
            var payload = new { TrnIDs = ids, T1 = DateTime.UtcNow };
            var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            var req     = await CreateAuthedRequest(HttpMethod.Post, "poll/ack", content);
            var (res, body) = await HttpLogger.SendAsync(_http, req, ctx, _logger);

            if (res.StatusCode == HttpStatusCode.Unauthorized)
            {
                _logger.Warn($"{ctx} | UNAUTHORIZED | Marking disconnected");
                _isConnected = false;
                return;
            }

            if (res.IsSuccessStatusCode)
                _logger.Info($"{ctx} | SUCCESS | IDs={string.Join(",", ids)}");
            else
                _logger.Error($"{ctx} | FAILED | Status={(int)res.StatusCode} | IDs={string.Join(",", ids)} | Body={body}");
        }
        catch (TaskCanceledException tcex) { _logger.Error($"{ctx} | TIMEOUT | {tcex.Message} | IDs={string.Join(",", ids)}"); }
        catch (HttpRequestException hre)   { _logger.Error($"{ctx} | HTTP-ERROR | {hre.Message} | IDs={string.Join(",", ids)}"); }
        catch (Exception ex)               { _logger.Error($"{ctx} | EXCEPTION | {ex.GetType().Name}: {ex.Message} | IDs={string.Join(",", ids)}"); }
    }

    private async Task TokenRefreshLoop()
    {
        var ctx = $"{_label} TOKEN-REFRESH";
        _logger.Debug($"{ctx} | Loop started");

        while (_isConnected)
        {
            await Task.Delay(TimeSpan.FromSeconds(30));
            if (!_isConnected) break;

            try
            {
                if (string.IsNullOrEmpty(_token))
                {
                    _logger.Warn($"{ctx} | Token is null/empty — cannot refresh, skipping");
                    continue;
                }

                DateTime expiry;
                try { expiry = JwtHelper.GetExpiry(_token); }
                catch (Exception jex)
                {
                    _logger.Error($"{ctx} | Failed to parse token expiry | {jex.Message}");
                    continue;
                }

                var secondsLeft = (expiry - DateTime.UtcNow).TotalSeconds;
                _logger.Debug($"{ctx} | TokenExpiresAt={expiry:HH:mm:ss} SecondsLeft={secondsLeft:F0}");

                if (secondsLeft >= 60)
                {
                    _logger.Debug($"{ctx} | Token still valid — skip refresh");
                    continue;
                }

                if (secondsLeft <= 0)
                    _logger.Warn($"{ctx} | Token already EXPIRED ({Math.Abs(secondsLeft):F0}s ago) — refreshing urgently");
                else
                    _logger.Info($"{ctx} | Token expiring soon ({secondsLeft:F0}s left) — refreshing");

                var req = new HttpRequestMessage(HttpMethod.Post, "auth/refresh");
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);

                var (res, body) = await HttpLogger.SendAsync(_http, req, ctx, _logger);

                if (!res.IsSuccessStatusCode)
                {
                    _refreshFailCount++;
                    _logger.Error($"{ctx} | REFRESH FAILED | Status={(int)res.StatusCode} | Body={body} | Fails={_refreshFailCount}");
                    if (res.StatusCode == HttpStatusCode.Unauthorized)
                    {
                        _logger.Warn($"{ctx} | 401 on refresh — marking disconnected");
                        _isConnected = false;
                    }
                    continue;
                }

                JsonElement doc;
                try { doc = JsonDocument.Parse(body).RootElement; }
                catch (JsonException jex)
                {
                    _logger.Error($"{ctx} | PARSE-ERROR on refresh response | {jex.Message}");
                    continue;
                }

                if (!doc.TryGetProperty("token", out var newTokenEl) || string.IsNullOrWhiteSpace(newTokenEl.GetString()))
                {
                    _logger.Missing(ctx, "token", "Refresh response missing token");
                    continue;
                }

                if (!doc.TryGetProperty("typeMID", out var newTypeMidEl))
                    _logger.Warn($"{ctx} | Refresh response missing typeMID");

                var newToken   = newTokenEl.GetString();
                var newTypeMid = newTypeMidEl.ValueKind != JsonValueKind.Undefined ? newTypeMidEl.GetString() : _typeMid;

                if (newTypeMid != _typeMid)
                    _logger.Mismatch(ctx, "typeMID", _typeMid, newTypeMid, isError: true);

                _token            = newToken;
                _typeMid          = newTypeMid;
                _refreshFailCount = 0;

                _logger.Info($"{ctx} | SUCCESS | NewExpiry={JwtHelper.GetExpiry(_token!):HH:mm:ss}");
            }
            catch (TaskCanceledException)
            {
                _refreshFailCount++;
                _logger.Warn($"{ctx} | Refresh request timed out | Fails={_refreshFailCount}");
                if (_refreshFailCount >= 2)
                {
                    _logger.Warn($"{ctx} | Marking disconnected after timeout");
                    _isConnected = false;
                }
            }
            catch (HttpRequestException hre) when (
                hre.InnerException is System.Net.Sockets.SocketException ||
                hre.Message.Contains("refused") ||
                hre.Message.Contains("No connection"))
            {
                _logger.Error($"{ctx} | SERVER-DOWN | {hre.Message} — marking disconnected immediately");
                _isConnected      = false;
                _refreshFailCount = 0;
            }
            catch (Exception ex)
            {
                _refreshFailCount++;
                _logger.Error($"{ctx} | EXCEPTION | {ex.GetType().Name}: {ex.Message} | Fails={_refreshFailCount}");
                if (_refreshFailCount >= 3)
                {
                    _logger.Warn($"{ctx} | Marking disconnected after repeated failures");
                    _isConnected = false;
                }
            }
        }

        _logger.Debug($"{ctx} | Loop stopped (IsConnected=false) — supervisor will reconnect");
    }

    private Task<HttpRequestMessage> CreateAuthedRequest(
        HttpMethod method, string url, HttpContent? content = null)
    {
        if (string.IsNullOrEmpty(_token))
            throw new InvalidOperationException($"No auth token available for {_label} — call Login() first");

        var req = new HttpRequestMessage(method, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        if (content != null) req.Content = content;
        return Task.FromResult(req);
    }
}