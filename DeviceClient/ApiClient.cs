using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

public class ApiClient
{
    private readonly HttpClient   _http;
    private readonly DeviceInfo   _device;
    private readonly DeviceLogger _logger;
    private readonly string       _label;

    // ── Per-instance session state ─────────────────────────────────────────────
    private bool          _isConnected      = false;
    private string?       _token;
    private int?          _deviceId;
    private int?          _deviceType;
    private List<decimal> _lastIds          = new();
    private int           _refreshFailCount = 0;

    // ── Fail-count / supervisor handshake ──────────────────────────────────────
    // Incremented by Poll and Event on any network failure.
    // Reset to 0 on any successful response.
    // When it reaches 3 the supervisor takes over; poll/event loops go silent.
    private int  _consecutiveFailCount = 0;
    private bool _supervisorActive     = false;   // set by ConnectionSupervisor

    // ── Back-off for login retries (used by Login itself, not the supervisor) ──
    private int _loginFailCount = 0;

    public ApiClient(string baseUrl, DeviceInfo device, DeviceLogger logger)
    {
        _device = device;
        _logger = logger;
        _label  = $"[{device.MACAddr}]";

        if (string.IsNullOrWhiteSpace(baseUrl))
            _logger.Error("INIT | BaseUrl is null or empty — HttpClient will not work correctly");

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

        _logger.Debug($"INIT | BaseUrl={baseUrl} DeviceType={device.DeviceType} IP={device.IPAddr}");
    }

    // ── Public surface ─────────────────────────────────────────────────────────

    public bool IsConnected      => _isConnected;
    public bool SupervisorActive => _supervisorActive;

    public int ConsecutiveFailCount => _consecutiveFailCount;

    public void MarkSupervisorActive()   => _supervisorActive = true;

    public void MarkSupervisorInactive() => _supervisorActive = false;

    // ──────────────────────────────────────────────────────────────────────────
    public async Task Login()
    {
        var ctx = "LOGIN";
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
            Content = new StringContent(
                JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };

        try
        {
            var (res, body) = await HttpLogger.SendAsync(_http, req, ctx, _logger);

            if (!res.IsSuccessStatusCode)
            {
                _loginFailCount++;
                _logger.Error(
                    $"{ctx} | FAILED | Status={(int)res.StatusCode} | Body={body} | FailCount={_loginFailCount}");
                _isConnected = false;
                return;
            }

            // ── Parse response JSON ──────────────────────────────────────────
            JsonElement doc;
            try { doc = JsonDocument.Parse(body).RootElement; }
            catch (JsonException jex)
            {
                _logger.Error($"{ctx} | PARSE-ERROR | {jex.Message} | Body={body}");
                _isConnected = false;
                return;
            }

            if (!doc.TryGetProperty("token", out var tokenEl) ||
                string.IsNullOrWhiteSpace(tokenEl.GetString()))
            {
                _logger.Missing(ctx, "token", "Login response missing 'token' field");
                _isConnected = false;
                return;
            }

            var newToken = tokenEl.GetString()!;

            // ── Extract deviceId / deviceType from JWT claims ────────────────
            int?  claimDeviceId   = null;
            int?  claimDeviceType = null;

            try
            {
                var claims = JwtHelper.GetClaims(newToken);

                if (claims.TryGetValue("deviceId", out var did) &&
                    int.TryParse(did, out var parsedId))
                    claimDeviceId = parsedId;
                else
                    _logger.Missing(ctx, "JWT:deviceId",
                        "Token does not contain a valid 'deviceId' claim");

                if (claims.TryGetValue("deviceType", out var dtype) &&
                    int.TryParse(dtype, out var parsedType))
                    claimDeviceType = parsedType;
                else
                    _logger.Missing(ctx, "JWT:deviceType",
                        "Token does not contain a valid 'deviceType' claim");
            }
            catch (Exception jex)
            {
                _logger.Error($"{ctx} | JWT-PARSE-FAILED | {jex.Message}");
                _isConnected = false;
                return;
            }

            // ── Mismatch guards ──────────────────────────────────────────────
            if (_token      != null && _token      != newToken)       _logger.Warn($"{ctx} | TOKEN CHANGED mid-session");
            if (_deviceId   != null && _deviceId   != claimDeviceId)  _logger.Mismatch(ctx, "deviceId",   _deviceId,   claimDeviceId,   isError: true);
            if (_deviceType != null && _deviceType != claimDeviceType) _logger.Mismatch(ctx, "deviceType", _deviceType, claimDeviceType, isError: true);

            _token      = newToken;
            _deviceId   = claimDeviceId;
            _deviceType = claimDeviceType;

            // ── Reset counters ───────────────────────────────────────────────
            _isConnected           = true;
            _loginFailCount        = 0;
            _consecutiveFailCount  = 0;

            _logger.Info(
                $"{ctx} | SUCCESS | DeviceID={_deviceId} DeviceType={_deviceType}");

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
            _logger.Error(
                $"{ctx} | EXCEPTION | {ex.GetType().Name}: {ex.Message} | FailCount={_loginFailCount}");
            _isConnected = false;
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    public async Task PollAndProcess()
    {
        var ctx = "POLL";

        // While the supervisor is active, poll must stay silent and do nothing.
        if (_supervisorActive) return;

        if (!_isConnected)
        {
            // Count this as a failure and wait before returning.
            RecordFailure(ctx);
            await Task.Delay(TimeSpan.FromSeconds(2));
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
                RecordFailure(ctx);
                return;
            }

            if (!res.IsSuccessStatusCode)
            {
                _logger.UnexpectedResponse(ctx, (int)res.StatusCode, body,
                    "Non-success from poll endpoint");
                RecordFailure(ctx);
                return;
            }

            // Success — reset the fail counter.
            ResetFailCount();

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
                        _logger.Warn(
                            $"{ctx} | ROW-WARN | TrnID=0 (may be invalid) | MsgStr={row.MsgStr}");
                    if (string.IsNullOrWhiteSpace(row.MsgStr))
                        _logger.Warn($"{ctx} | ROW-WARN | TrnID={row.TrnID} has empty MsgStr");
                    if (row.RetryCnt > 0)
                        _logger.Warn(
                            $"{ctx} | ROW-RETRY | TrnID={row.TrnID} RetryCnt={row.RetryCnt}");
                    ids.Add(row.TrnID);
                }

                _lastIds = ids;
                _logger.Info(
                    $"{ctx} | DATA | Count={ids.Count} TotalPending={poll.TotalPending} IDs={string.Join(",", ids)}");

                await Ack(ids);
                _lastIds.Clear();
            }
            else
            {
                _logger.Debug($"{ctx} | NO-DATA | TotalPending={poll.TotalPending}");
            }
        }
        catch (TaskCanceledException tcex)
        {
            _logger.Error($"{ctx} | TIMEOUT | {tcex.Message}");
            RecordFailure(ctx);
        }
        catch (HttpRequestException hre)
        {
            _logger.Error($"{ctx} | HTTP-ERROR | {hre.Message}");
            RecordFailure(ctx);
        }
        catch (Exception ex)
        {
            _logger.Error($"{ctx} | EXCEPTION | {ex.GetType().Name}: {ex.Message}");
            RecordFailure(ctx);
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    public async Task SendEventAsync(string message, int counter)
    {
        var ctx = "EVENT";

        // While the supervisor is active, event must stay silent and do nothing.
        if (_supervisorActive) return;

        if (!_isConnected)
        {
            RecordFailure(ctx);
            await Task.Delay(TimeSpan.FromSeconds(2));
            return;
        }

        var payload = new
        {
            DeviceID   = _deviceId,
            DeviceType = _deviceType,
            Message    = message,
            T1         = DateTime.UtcNow,
            EventSeqNo = counter
        };

        var content = new StringContent(
            JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

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
                RecordFailure(ctx);
                return;
            }

            if (!res.IsSuccessStatusCode)
            {
                _logger.UnexpectedResponse(ctx, (int)res.StatusCode, body, "Event rejected");
                RecordFailure(ctx);
                return;
            }

            // Success.
            ResetFailCount();
        }
        catch (Exception ex)
        {
            _logger.Error($"{ctx} | EXCEPTION | {ex.Message}");
            RecordFailure(ctx);

            if (ex is HttpRequestException hre &&
                (hre.InnerException is System.Net.Sockets.SocketException ||
                 hre.Message.Contains("refused")))
            {
                _logger.Warn($"{ctx} | SERVER-DOWN detected in event — marking disconnected");
                _isConnected = false;
            }
        }
    }

    // ── Private helpers ────────────────────────────────────────────────────────

    private void RecordFailure(string ctx)
    {
        _consecutiveFailCount++;
        _logger.Warn(
            $"{ctx} | REQUEST-FAIL | ConsecutiveFailCount={_consecutiveFailCount} " +
            $"(supervisor takes over at 3)");
    }

    private void ResetFailCount()
    {
        if (_consecutiveFailCount > 0)
            _logger.Info($"REQUEST-SUCCESS | ConsecutiveFailCount reset (was {_consecutiveFailCount})");
        _consecutiveFailCount = 0;
    }

    // ──────────────────────────────────────────────────────────────────────────
    private async Task Ack(List<decimal> ids)
    {
        var ctx = "ACK";

        if (ids == null || ids.Count == 0)
        {
            _logger.Warn($"{ctx} | SKIPPED | Empty IDs list");
            return;
        }

        try
        {
            var payload = new { TrnIDs = ids, T1 = DateTime.UtcNow };
            var content = new StringContent(
                JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
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
                _logger.Error(
                    $"{ctx} | FAILED | Status={(int)res.StatusCode} | IDs={string.Join(",", ids)} | Body={body}");
        }
        catch (TaskCanceledException tcex)
        {
            _logger.Error($"{ctx} | TIMEOUT | {tcex.Message} | IDs={string.Join(",", ids)}");
        }
        catch (HttpRequestException hre)
        {
            _logger.Error($"{ctx} | HTTP-ERROR | {hre.Message} | IDs={string.Join(",", ids)}");
        }
        catch (Exception ex)
        {
            _logger.Error(
                $"{ctx} | EXCEPTION | {ex.GetType().Name}: {ex.Message} | IDs={string.Join(",", ids)}");
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    private async Task TokenRefreshLoop()
    {
        var ctx = "TOKEN-REFRESH";
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
                _logger.Debug(
                    $"{ctx} | TokenExpiresAt={expiry:HH:mm:ss} SecondsLeft={secondsLeft:F0}");

                if (secondsLeft >= 60)
                {
                    _logger.Debug($"{ctx} | Token still valid — skip refresh");
                    continue;
                }

                if (secondsLeft <= 0)
                    _logger.Warn(
                        $"{ctx} | Token already EXPIRED ({Math.Abs(secondsLeft):F0}s ago) — refreshing urgently");
                else
                    _logger.Info($"{ctx} | Token expiring soon ({secondsLeft:F0}s left) — refreshing");

                var req = new HttpRequestMessage(HttpMethod.Post, "auth/refresh");
                req.Headers.Authorization =
                    new AuthenticationHeaderValue("Bearer", _token);

                var (res, body) = await HttpLogger.SendAsync(_http, req, ctx, _logger);

                if (!res.IsSuccessStatusCode)
                {
                    _refreshFailCount++;
                    _logger.Error(
                        $"{ctx} | REFRESH FAILED | Status={(int)res.StatusCode} | Body={body} | Fails={_refreshFailCount}");
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

                if (!doc.TryGetProperty("token", out var newTokenEl) ||
                    string.IsNullOrWhiteSpace(newTokenEl.GetString()))
                {
                    _logger.Missing(ctx, "token", "Refresh response missing token");
                    continue;
                }

                var newToken = newTokenEl.GetString()!;

                // Re-extract claims from the refreshed token.
                try
                {
                    var claims = JwtHelper.GetClaims(newToken);

                    if (claims.TryGetValue("deviceId", out var did) &&
                        int.TryParse(did, out var parsedId))
                    {
                        if (_deviceId.HasValue && _deviceId != parsedId)
                            _logger.Mismatch(ctx, "JWT:deviceId", _deviceId, parsedId, isError: true);
                        _deviceId = parsedId;
                    }

                    if (claims.TryGetValue("deviceType", out var dtype) &&
                        int.TryParse(dtype, out var parsedType))
                    {
                        if (_deviceType.HasValue && _deviceType != parsedType)
                            _logger.Mismatch(ctx, "JWT:deviceType", _deviceType, parsedType, isError: true);
                        _deviceType = parsedType;
                    }
                }
                catch (Exception jex)
                {
                    _logger.Error($"{ctx} | JWT-CLAIM-PARSE-FAILED on refreshed token | {jex.Message}");
                }

                _token            = newToken;
                _refreshFailCount = 0;

                _logger.Info(
                    $"{ctx} | SUCCESS | NewExpiry={JwtHelper.GetExpiry(_token!):HH:mm:ss} " +
                    $"DeviceID={_deviceId} DeviceType={_deviceType}");
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
                _logger.Error(
                    $"{ctx} | SERVER-DOWN | {hre.Message} — marking disconnected immediately");
                _isConnected      = false;
                _refreshFailCount = 0;
            }
            catch (Exception ex)
            {
                _refreshFailCount++;
                _logger.Error(
                    $"{ctx} | EXCEPTION | {ex.GetType().Name}: {ex.Message} | Fails={_refreshFailCount}");
                if (_refreshFailCount >= 3)
                {
                    _logger.Warn($"{ctx} | Marking disconnected after repeated failures");
                    _isConnected = false;
                }
            }
        }

        _logger.Debug(
            $"{ctx} | Loop stopped (IsConnected=false) — supervisor will reconnect");
    }

    // ──────────────────────────────────────────────────────────────────────────
    private Task<HttpRequestMessage> CreateAuthedRequest(
        HttpMethod method, string url, HttpContent? content = null)
    {
        if (string.IsNullOrEmpty(_token))
            throw new InvalidOperationException(
                "No auth token available — call Login() first");

        var req = new HttpRequestMessage(method, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        if (content != null) req.Content = content;
        return Task.FromResult(req);
    }
}