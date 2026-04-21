using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

public class ApiClient
{
    private readonly HttpClient _http;

    public ApiClient(string baseUrl)
    {
        var handler = new SocketsHttpHandler
        {
            KeepAlivePingDelay    = TimeSpan.FromSeconds(15),
            KeepAlivePingTimeout  = TimeSpan.FromSeconds(5),
            EnableMultipleHttp2Connections = true,
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
        };

        _http = new HttpClient(handler);
        _http.BaseAddress = new Uri(baseUrl);
    }

    public async Task Login(int deviceType, string mac, string ip)
    {
        DeviceLogger.Info($"LOGIN START | DeviceType={deviceType} MAC={mac} IP={ip}");

        var payload = new { DeviceType = deviceType, MACAddr = mac, IPAddr = ip, T1 = DateTime.UtcNow };
        var jsonPayload = JsonSerializer.Serialize(payload);

        var req = new HttpRequestMessage(HttpMethod.Post, "auth/login")
        {
            Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json")
        };

        try
        {
            var (res, body) = await HttpLogger.SendAsync(_http, req, "LOGIN");

            DeviceLogger.Debug($"LOGIN RESPONSE BODY ← {body}");

            if (!res.IsSuccessStatusCode)
            {
                DeviceLogger.Error($"LOGIN FAILED | Body={body}");

                DeviceState.SetDisconnected("Login failed");
                return;
            }

            var doc = JsonDocument.Parse(body).RootElement;

            DeviceSession.Token   = doc.GetProperty("token").GetString();
            DeviceSession.TypeMID = doc.GetProperty("typeMID").GetString();
            DeviceSession.DeviceId = doc.GetProperty("deviceId").GetInt32();

            DeviceLogger.Info("LOGIN SUCCESS");

            DeviceState.SetConnected();

            await EnsureTokenFreshAsync();
            StartBackgroundServicesOnce();  
        }
        catch (Exception ex)
        {
            DeviceLogger.Error($"LOGIN ERROR | {ex}");

            DeviceState.SetDisconnected("Login failed");
        }
    }

    public async Task Restore()
    {
        var req = await CreateAuthedRequest(HttpMethod.Post, "poll/restore");
        await _http.SendAsync(req);
    }

    public async Task PollAndProcess()
    {
        var action = "POLL";

        try
        {
            DeviceLogger.Info($"{action} | Started polling");

            var req = await CreateAuthedRequest(HttpMethod.Get, "poll");

            var (res, body) = await HttpLogger.SendAsync(_http, req, action);

            if (res.StatusCode == HttpStatusCode.Unauthorized)
            {
                DeviceLogger.Error("401 received → marking disconnected");
                DeviceState.SetDisconnected("401 from server");
                return;
            }

            if (!res.IsSuccessStatusCode)
            {
                var msg = ReadMessage(body);

                DeviceLogger.Error(
                    $"{action} | FAILED | ServerMessage={msg} | Response={res} | Body={body}");

                return;
            }

            var poll = JsonSerializer.Deserialize<PollResponse>(body,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (poll == null)
            {
                DeviceLogger.Error($"{action} | Deserialization returned NULL");
                return;
            }

            // ---------------- ACK-FIRST ----------------
            if (poll.NeedAckFirst)
            {
                DeviceLogger.Info($"{action} | ACK-FIRST requested by server");

                if (DeviceMemory.LastIds.Count > 0)
                {
                    DeviceLogger.Info(
                        $"{action} | Sending previous ACK | TrnIDs={string.Join(",", DeviceMemory.LastIds)}");

                    await Ack(DeviceMemory.LastIds);

                    DeviceMemory.LastIds.Clear();
                }
                else
                {
                    DeviceLogger.Info($"{action} | ACK-FIRST but no pending IDs");
                }

                return;
            }

            // ---------------- DATA RECEIVED ----------------
            if (poll.HasData && poll.Rows.Count > 0)
            {
                var ids = poll.Rows.Select(x => x.TrnID).ToList();

                DeviceLogger.Info(
                    $"{action} | DATA RECEIVED | Count: {ids.Count} | TrnIDs: {string.Join(",", ids)}");

                DeviceLogger.Debug(
                    $"{action} | FullRows: {JsonSerializer.Serialize(poll.Rows)}");

                DeviceMemory.LastIds = ids;

                DeviceLogger.Info($"{action} | Sending ACK immediately for received data");

                await Ack(ids);

                DeviceMemory.LastIds.Clear();

            }
            else
            {
                DeviceLogger.Info($"{action} | No data from server");
            }
        }
        catch (Exception ex)
        {
            DeviceLogger.Error(
                $"{action} | EXCEPTION | Message: {ex.Message} | StackTrace: {ex.StackTrace} | Exception: {ex}");
        }
    }

    public async Task SendEventAsync(string message)
    {
        var action = "EVENT";

        try
        {
            DeviceLogger.Info($"{action} | Preparing to send event | Message: {message}");

            var payload = new
            {
                TypeMID   = DeviceSession.TypeMID,
                Message   = message,
                T1        = DateTime.UtcNow, 
            };

            var jsonPayload = JsonSerializer.Serialize(payload);

            DeviceLogger.Debug(
                $"{action} | Payload: {jsonPayload}");

            var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
            
            var req = await CreateAuthedRequest(HttpMethod.Post, "poll/event", content);

            var (res, body) = await HttpLogger.SendAsync(_http, req, action);

            var ackMsg = ReadMessage(body);

            if (res.StatusCode == HttpStatusCode.Unauthorized)
            {
                DeviceLogger.Error("401 received → marking disconnected");
                DeviceState.SetDisconnected("401 from server");
                return;
            }

            if (res.IsSuccessStatusCode)
            {
                DeviceLogger.Info(
                    $"{action} | ACK RECEIVED | ServerMessage={ackMsg}");
            }
            else
            {
                DeviceLogger.Error(
                    $"{action} | FAILED | Status={(int)res.StatusCode} | ServerMessage={ackMsg} | Response={res} | Body={jsonPayload}");
            }
        }
        catch (Exception ex)
        {
            DeviceLogger.Error(
                $"{action} | EXCEPTION | Message={ex.Message} | StackTrace={ex.StackTrace} | Exception={ex}");
        }
    }

    private async Task Ack(List<decimal> ids)
    {
        var action = "ACK";

        try
        {
            DeviceLogger.Info(
                $"{action} | Preparing ACK | TrnIDs={string.Join(",", ids)}");

            var payload = new { TrnIDs = ids, T1 = DateTime.UtcNow };
            var jsonPayload = JsonSerializer.Serialize(payload);

            DeviceLogger.Debug(
                $"{action} | Payload: {jsonPayload}");

            var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

            var req = await CreateAuthedRequest(HttpMethod.Post, "poll/ack", content);

            var (res, body) = await HttpLogger.SendAsync(_http, req, action);

            var message = ReadMessage(body);

            if (res.StatusCode == HttpStatusCode.Unauthorized)
            {
                DeviceLogger.Error("401 received → marking disconnected");
                DeviceState.SetDisconnected("401 from server");
                return;
            }

            if (res.IsSuccessStatusCode)
            {
                DeviceLogger.Info(
                    $"{action} | SUCCESS | ServerMessage={message}");
            }
            else
            {
                DeviceLogger.Error(
                    $"{action} | FAILED | Status={(int)res.StatusCode} | ServerMessage={message} | Response={res} | Body={jsonPayload} | Body={body}");
            }
        }
        catch (Exception ex)
        {
            DeviceLogger.Error(
                $"{action} | EXCEPTION | Message={ex.Message} | StackTrace={ex.StackTrace} | Exception={ex}");
        }
    }

    // Auto refresh before expiry
    private async Task EnsureTokenFreshAsync()
    {
        var action = "TOKEN & AUTHENTICATION CHECK";

        try
        {
            if (string.IsNullOrEmpty(DeviceSession.Token))
            {
                DeviceLogger.Debug($"{action} | No token present, skipping refresh check");
                return;
            }

            var expiry = JwtHelper.GetExpiry(DeviceSession.Token!);
            var secondsLeft = (expiry - DateTime.UtcNow).TotalSeconds;

            DeviceLogger.Debug(
                $"{action} | Token expiry check | SecondsLeft={secondsLeft}");

            if (secondsLeft >= 60)
                return;

            DeviceLogger.Info(
                $"{action} | Token expiring soon | SecondsLeft={secondsLeft}");

            var start = DateTime.UtcNow;

            var req = new HttpRequestMessage(HttpMethod.Post, "auth/refresh");
            req.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", DeviceSession.Token);

            var (res, body) = await HttpLogger.SendAsync(_http, req, action);

            var end = DateTime.UtcNow;

            var message = ReadMessage(body);

            if (!res.IsSuccessStatusCode)
            {
                DeviceLogger.Error(
                    $"{action} | FAILED | Status={(int)res.StatusCode} | ServerMessage={message} | Response={res} | Body={body}");

                return;
            }

            DeviceLogger.Debug($"{action} | RawResponse: {body} | Response: {res} | Start: {start:HH:mm:ss.fff} | End: {end:HH:mm:ss.fff} | DurationMs={(end - start).TotalMilliseconds} ms");

            var doc = JsonDocument.Parse(body).RootElement;

            DeviceSession.Token   = doc.GetProperty("token").GetString();
            DeviceSession.TypeMID = doc.GetProperty("typeMID").GetString();

            DeviceLogger.Info(
                $"{action} | SUCCESS | ServerMessage={message}");
        }
        catch (Exception ex)
        {
            DeviceLogger.Error(
                $"{action} | EXCEPTION | Message={ex.Message} | StackTrace={ex.StackTrace} | Exception={ex}");
        }
    }

    // reading the response message for log
    private string ReadMessage(string body)
    {
        var action = "READ-MESSAGE";

        try
        {
            var doc = JsonDocument.Parse(body).RootElement;

            if (doc.TryGetProperty("message", out var msg))
            {
                var text = msg.GetString() ?? body;

                return text;
            }

            if (doc.TryGetProperty("Message", out var msg2))
            {
                var text = msg2.GetString() ?? body;

                return text;
            }

            DeviceLogger.Debug(
                $"{action} | No message field found, returning full body");

            return body;
        }
        catch (Exception ex)
        {
            DeviceLogger.Error(
                $"{action} | Failed to parse JSON | Message={ex.Message} | Returning raw body: {body} | Exception={ex}");
            return body;
        }
    }

    private async Task<HttpRequestMessage> CreateAuthedRequest(HttpMethod method, string url, HttpContent? content = null)
    {
        var req = new HttpRequestMessage(method, url);

        if (string.IsNullOrEmpty(DeviceSession.Token))
            throw new InvalidOperationException("No token for authed request");

        req.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", DeviceSession.Token);

        if (content != null)
            req.Content = content;

        return req;
    }

    private void StartBackgroundServices()
    {
        // TOKEN REFRESHER
        _ = Task.Run(async () =>
        {
            while (true)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(30));

                    if (!DeviceState.IsConnected)
                        continue;

                    await EnsureTokenFreshAsync();
                }
                catch { }
            }
        });
    }

    private void StartBackgroundServicesOnce()
    {
        if (_bgStarted) return;
        _bgStarted = true;

        StartBackgroundServices();
    }
    private bool _bgStarted = false;
}