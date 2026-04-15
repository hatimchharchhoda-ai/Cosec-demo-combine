using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

public class ApiClient
{
    private readonly HttpClient _http;

    public ApiClient()
    {
        _http = new HttpClient();
        _http.BaseAddress = new Uri("http://localhost:5000/api/");
    }

    // 🔹 ALWAYS called before any API call
    private async Task AddAuthAsync()
    {
        await EnsureTokenFreshAsync();

        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", DeviceSession.Token);
    }

    public async Task Login(int deviceId, string mac, string ip)
    {
        var payload = new { DeviceID = deviceId, MACAddr = mac, IPAddr = ip };

        var res = await _http.PostAsync("auth/login",
            new StringContent(JsonSerializer.Serialize(payload),
            Encoding.UTF8, "application/json"));

        var json = await res.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json).RootElement;

        DeviceSession.Token   = doc.GetProperty("token").GetString();
        DeviceSession.TypeMID = doc.GetProperty("typeMID").GetString();

        DeviceLogger.Log($"LOGIN SUCCESS | TypeMID={DeviceSession.TypeMID}");
    }

    public async Task Restore()
    {
        await AddAuthAsync();
        await _http.PostAsync("poll/restore", null);

        DeviceLogger.Log($"RESTORE called | TypeMID={DeviceSession.TypeMID}");
    }

    public async Task PollAndProcess()
    {
        try
        {
            await AddAuthAsync();

            var res  = await _http.GetAsync("poll");
            var json = await res.Content.ReadAsStringAsync();

            var poll = JsonSerializer.Deserialize<PollResponse>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (poll == null) return;

            // ACK-FIRST case
            if (poll.NeedAckFirst)
            {
                if (DeviceMemory.LastIds.Count > 0)
                {
                    DeviceLogger.Log($"ACK-FIRST | TypeMID={DeviceSession.TypeMID}");
                    await Ack(DeviceMemory.LastIds);
                    DeviceMemory.LastIds.Clear();
                }
                return;
            }

            // DATA case
            if (poll.HasData && poll.Rows.Count > 0)
            {
                var ids = poll.Rows.Select(x => x.TrnID).ToList();

                DeviceLogger.Log($"DATA RECEIVED | TypeMID={DeviceSession.TypeMID} | {string.Join(",", ids)}");

                DeviceMemory.LastIds = ids;

                await Task.Delay(2000);

                await Ack(ids);

                DeviceMemory.LastIds.Clear();
            }
        }
        catch (Exception ex)
        {
            DeviceLogger.Log($"ERROR | TypeMID={DeviceSession.TypeMID} | {ex.Message}");
        }
    }

    private async Task Ack(List<decimal> ids)
    {
        await AddAuthAsync();

        var payload = new { TrnIDs = ids };

        var res = await _http.PostAsync("poll/ack",
            new StringContent(JsonSerializer.Serialize(payload),
            Encoding.UTF8, "application/json"));

        if (res.IsSuccessStatusCode)
            DeviceLogger.Log($"ACK SENT | TypeMID={DeviceSession.TypeMID}");
        else
            DeviceLogger.Log($"ACK FAILED | TypeMID={DeviceSession.TypeMID}");
    }

    // 🔥 Auto refresh before expiry
    private async Task EnsureTokenFreshAsync()
    {
        if (string.IsNullOrEmpty(DeviceSession.Token))
            return;

        var expiry = JwtHelper.GetExpiry(DeviceSession.Token!);

        if ((expiry - DateTime.UtcNow).TotalSeconds < 60)
        {
            DeviceLogger.Log($"TOKEN EXPIRING | TypeMID={DeviceSession.TypeMID}");

            var res  = await _http.PostAsync("auth/refresh", null);
            var json = await res.Content.ReadAsStringAsync();

            var doc = JsonDocument.Parse(json).RootElement;

            DeviceSession.Token   = doc.GetProperty("token").GetString();
            DeviceSession.TypeMID = doc.GetProperty("typeMID").GetString();

            DeviceLogger.Log($"TOKEN REFRESHED | TypeMID={DeviceSession.TypeMID}");
        }
    }
}