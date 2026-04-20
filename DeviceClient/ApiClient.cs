using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

public class ApiClient
{
    private readonly HttpClient _http;

    public ApiClient(string baseUrl)
    {
        _http = new HttpClient();
        _http.BaseAddress = new Uri(baseUrl);

        StartBackgroundServices();
    }

    public async Task Login(int deviceId, string mac, string ip)
    {
        DeviceLogger.Info($"LOGIN START | DeviceId={deviceId} MAC={mac} IP={ip}");

        var payload = new { DeviceID = deviceId, MACAddr = mac, IPAddr = ip, T1 = DateTime.Now };
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
                return;
            }

            var doc = JsonDocument.Parse(body).RootElement;

            DeviceSession.Token   = doc.GetProperty("token").GetString();
            DeviceSession.TypeMID = doc.GetProperty("typeMID").GetString();

            DeviceLogger.Info("LOGIN SUCCESS");
        }
        catch (Exception ex)
        {
            DeviceLogger.Error($"LOGIN ERROR | {ex}");
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

                _ackChannel.Writer.TryWrite(ids);

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
                T1        = DateTime.Now, 
            };

            var jsonPayload = JsonSerializer.Serialize(payload);

            DeviceLogger.Debug(
                $"{action} | Payload: {jsonPayload}");

            var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
            
            var req = await CreateAuthedRequest(HttpMethod.Post, "poll/event", content);

            var (res, body) = await HttpLogger.SendAsync(_http, req, action);

            var ackMsg = ReadMessage(body);

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

            var payload = new { TrnIDs = ids, T1 = DateTime.Now };
            var jsonPayload = JsonSerializer.Serialize(payload);

            DeviceLogger.Debug(
                $"{action} | Payload: {jsonPayload}");

            var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

            var req = await CreateAuthedRequest(HttpMethod.Post, "poll/ack", content);

            var (res, body) = await HttpLogger.SendAsync(_http, req, action);

            var message = ReadMessage(body);

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

            var start = DateTime.Now;

            var req = new HttpRequestMessage(HttpMethod.Post, "auth/refresh");
            req.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", DeviceSession.Token);

            var (res, body) = await HttpLogger.SendAsync(_http, req, action);

            var end = DateTime.Now;

            var message = ReadMessage(body);

            if (!res.IsSuccessStatusCode)
            {
                DeviceLogger.Error(
                    $"{action} | FAILED | Status={(int)res.StatusCode} | ServerMessage={message} | Response={res} | Body={body}");

                DeviceSession.Token = null;
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
                    await EnsureTokenFreshAsync();
                }
                catch { }
            }
        });

        // ACK WORKER
        _ = Task.Run(ProcessAckQueue);
    }

    private async Task ProcessAckQueue()
    {
        await foreach (var ids in _ackChannel.Reader.ReadAllAsync())
        {
            try
            {
                await Ack(ids);
                
                DeviceLogger.Info($"ACK BG | ACK completed for received data");
            }
            catch (Exception ex)
            {
                DeviceLogger.Error($"ACK WORKER ERROR | {ex}");
            }
        }
    }

    private readonly Channel<List<decimal>> _ackChannel =
        Channel.CreateUnbounded<List<decimal>>();
}