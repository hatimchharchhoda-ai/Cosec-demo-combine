using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

public class ApiClient
{
    private readonly HttpClient _http;

    public ApiClient(string baseUrl)
    {
        _http = new HttpClient();
        _http.BaseAddress = new Uri(baseUrl);
    }

    public async Task Login(int deviceId, string mac, string ip)
    {
        DeviceLogger.Info($"LOGIN START | DeviceId={deviceId} MAC={mac} IP={ip}");

        var payload = new { DeviceID = deviceId, MACAddr = mac, IPAddr = ip };
        var body = JsonSerializer.Serialize(payload);

        DeviceLogger.Debug($"LOGIN REQUEST → {body}");

        HttpResponseMessage res;

        try
        {
            res = await _http.PostAsync("auth/login",
                new StringContent(body, Encoding.UTF8, "application/json"));
        }
        catch (Exception ex)
        {
            DeviceLogger.Error($"LOGIN HTTP ERROR | {ex.Message}");
            return;
        }

        DeviceLogger.Debug($"LOGIN RESPONSE STATUS ← {(int)res.StatusCode}");

        var message = await ReadMessageAsync(res);
        DeviceLogger.Debug($"LOGIN RESPONSE MESSAGE ← {message}");

        if (!res.IsSuccessStatusCode)
        {
            DeviceLogger.Error($"LOGIN FAILED | {message}");
            return;
        }

        var json = await res.Content.ReadAsStringAsync();
        DeviceLogger.Debug($"LOGIN RESPONSE BODY ← {json}");

        try
        {
            var doc = JsonDocument.Parse(json).RootElement;

            DeviceSession.Token   = doc.GetProperty("token").GetString();
            DeviceSession.TypeMID = doc.GetProperty("typeMID").GetString();

            DeviceLogger.Info($"LOGIN SUCCESS");
        }
        catch (Exception ex)
        {
            DeviceLogger.Error($"LOGIN PARSE ERROR | {ex.Message}");
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

            var res = await HttpLogger.SendAsync(_http, req, action);

            var json = await res.Content.ReadAsStringAsync();

            if (!res.IsSuccessStatusCode)
            {
                var msg = await ReadMessageAsync(res);

                DeviceLogger.Error(
                    $"{action} | FAILED | Status={(int)res.StatusCode}\n" +
                    $"ServerMessage={msg}");

                return;
            }

            DeviceLogger.Debug($"{action} | RawResponse={json}");

            var poll = JsonSerializer.Deserialize<PollResponse>(json,
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
                    $"{action} | DATA RECEIVED | Count={ids.Count} | TrnIDs={string.Join(",", ids)}");

                DeviceLogger.Debug(
                    $"{action} | FullRows=\n{JsonSerializer.Serialize(poll.Rows)}");

                DeviceMemory.LastIds = ids;

                DeviceLogger.Debug($"{action} | Waiting 2 seconds before ACK");
                await Task.Delay(2000);

                await Ack(ids);

                DeviceMemory.LastIds.Clear();

                DeviceLogger.Info($"{action} | ACK completed for received data");
            }
            else
            {
                DeviceLogger.Info($"{action} | No data from server");
            }
        }
        catch (Exception ex)
        {
            DeviceLogger.Error(
                $"{action} | EXCEPTION\n" +
                $"Message={ex.Message}\n" +
                $"StackTrace={ex.StackTrace}");
        }
    }

    public async Task SendEventAsync(string message)
    {
        var action = "EVENT";

        try
        {
            DeviceLogger.Info($"{action} | Preparing to send event | Message={message}");

            var payload = new
            {
                TypeMID   = DeviceSession.TypeMID,
                Message   = message,
                EventTime = DateTime.Now
            };

            var jsonPayload = JsonSerializer.Serialize(payload);

            DeviceLogger.Debug(
                $"{action} | Payload=\n{jsonPayload}");

            var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
            var req = await CreateAuthedRequest(HttpMethod.Post, "poll/event", content);

            var start = DateTime.Now;

            var res = await HttpLogger.SendAsync(_http, req, action);

            var end = DateTime.Now;

            var ackMsg = await ReadMessageAsync(res);

            if (res.IsSuccessStatusCode)
            {
                DeviceLogger.Info(
                    $"{action} | ACK RECEIVED | ServerMessage={ackMsg}");

                DeviceLogger.Debug(
                    $"{action} | Event sent Time: {start:HH:mm:ss.fff} | Event Timer between Start and End: {((end - start).TotalMilliseconds)} ms | Server Message: {ackMsg} | Ack Time: {end:HH:mm:ss.fff}");
            }
            else
            {
                DeviceLogger.Error(
                    $"{action} | FAILED | Status={(int)res.StatusCode}\n" +
                    $"ServerMessage={ackMsg}");
            }
        }
        catch (Exception ex)
        {
            DeviceLogger.Error(
                $"{action} | EXCEPTION\n" +
                $"Message={ex.Message}\n" +
                $"StackTrace={ex.StackTrace}");
        }
    }

    private async Task Ack(List<decimal> ids)
    {
        var action = "ACK";

        try
        {
            DeviceLogger.Info(
                $"{action} | Preparing ACK | TrnIDs={string.Join(",", ids)}");

            var payload = new { TrnIDs = ids };
            var jsonPayload = JsonSerializer.Serialize(payload);

            DeviceLogger.Debug(
                $"{action} | Payload=\n{jsonPayload}");

            var content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");
            var req = await CreateAuthedRequest(HttpMethod.Post, "poll/ack", content);

            var start = DateTime.Now;

            var res = await HttpLogger.SendAsync(_http, req, action);

            var end = DateTime.Now;

            var message = await ReadMessageAsync(res);

            if (res.IsSuccessStatusCode)
            {
                DeviceLogger.Info(
                    $"{action} | SUCCESS | ServerMessage={message}");

                DeviceLogger.Debug(
                    $"{action} | Start: {start:HH:mm:ss.fff} | DurationMs={(end - start).TotalMilliseconds} ms | Server Message: {message} | End: {end:HH:mm:ss.fff}");
            }
            else
            {
                DeviceLogger.Error(
                    $"{action} | FAILED | Status={(int)res.StatusCode}\n" +
                    $"ServerMessage={message}");
            }
        }
        catch (Exception ex)
        {
            DeviceLogger.Error(
                $"{action} | EXCEPTION\n" +
                $"Message={ex.Message}\n" +
                $"StackTrace={ex.StackTrace}");
        }
    }

    // Auto refresh before expiry
    private async Task EnsureTokenFreshAsync()
    {
        var action = "TOKEN-REFRESH";

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


            var req = new HttpRequestMessage(HttpMethod.Post, "auth/refresh");
            req.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", DeviceSession.Token);

            var start = DateTime.Now;

            var res = await HttpLogger.SendAsync(_http, req, action);

            var end = DateTime.Now;

            var message = await ReadMessageAsync(res);

            if (!res.IsSuccessStatusCode)
            {
                DeviceLogger.Error(
                    $"{action} | FAILED | Status={(int)res.StatusCode}\n" +
                    $"ServerMessage={message}");

                DeviceSession.Token = null;
                return;
            }

            var json = await res.Content.ReadAsStringAsync();

            DeviceLogger.Debug($"{action} | RawResponse=\n{json}");

            var doc = JsonDocument.Parse(json).RootElement;

            DeviceSession.Token   = doc.GetProperty("token").GetString();
            DeviceSession.TypeMID = doc.GetProperty("typeMID").GetString();

            DeviceLogger.Info(
                $"{action} | SUCCESS | ServerMessage={message}");

            DeviceLogger.Debug(
                $"{action} | Start: {start:HH:mm:ss.fff} | DurationMs={(end - start).TotalMilliseconds} ms | Server Message: {message} | End: {end:HH:mm:ss.fff}");
        }
        catch (Exception ex)
        {
            DeviceLogger.Error(
                $"{action} | EXCEPTION\n" +
                $"Message={ex.Message}\n" +
                $"StackTrace={ex.StackTrace}");
        }
    }

    // reading the response message for log
    private async Task<string> ReadMessageAsync(HttpResponseMessage res)
    {
        var action = "READ-MESSAGE";

        var body = await res.Content.ReadAsStringAsync();

        try
        {
            DeviceLogger.Debug(
                $"{action} | RawBody=\n{body}");

            var doc = JsonDocument.Parse(body).RootElement;

            if (doc.TryGetProperty("message", out var msg))
            {
                var text = msg.GetString() ?? body;

                DeviceLogger.Debug(
                    $"{action} | Extracted 'message' = {text}");

                return text;
            }

            if (doc.TryGetProperty("Message", out var msg2))
            {
                var text = msg2.GetString() ?? body;

                DeviceLogger.Debug(
                    $"{action} | Extracted 'Message' = {text}");

                return text;
            }

            DeviceLogger.Debug(
                $"{action} | No message field found, returning full body");

            return body;
        }
        catch (Exception ex)
        {
            DeviceLogger.Error(
                $"{action} | Failed to parse JSON\n" +
                $"Message={ex.Message}\n" +
                $"Returning raw body");

            return body;
        }
    }

    private async Task<HttpRequestMessage> CreateAuthedRequest(
        HttpMethod method, string url, HttpContent? content = null)
    {
        await EnsureTokenFreshAsync();

        var req = new HttpRequestMessage(method, url);

        req.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", DeviceSession.Token);

        if (content != null)
            req.Content = content;

        return req;
    }
}