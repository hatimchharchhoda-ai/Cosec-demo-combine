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

    private void AddAuth()
    {
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

        DeviceSession.Token = doc.GetProperty("token").GetString();
        DeviceSession.TypeMID = doc.GetProperty("typeMID").GetString();

        DeviceLogger.Log($"LOGIN SUCCESS | TypeMID={DeviceSession.TypeMID}");
    }

    public async Task Restore()
    {
        AddAuth();
        await _http.PostAsync("poll/restore", null);
        DeviceLogger.Log("RESTORE called");
    }

    public async Task PollAndProcess()
    {
        try
        {
            AddAuth();

            var res = await _http.GetAsync("poll");
            var json = await res.Content.ReadAsStringAsync();

            var poll = JsonSerializer.Deserialize<PollResponse>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (poll == null) return;

            // CASE 1 — Need ACK first
            if (poll.NeedAckFirst)
            {
                if (DeviceMemory.LastIds.Count > 0)
                {
                    DeviceLogger.Log($"ACK-FIRST received. Sending ACK. | TypeMID={DeviceSession.TypeMID} | {string.Join(",", DeviceMemory.LastIds)}");
                    await Ack(DeviceMemory.LastIds);
                    DeviceMemory.LastIds.Clear();
                }
                return;
            }

            // CASE 2 — New data
            if (poll.HasData && poll.Rows.Count > 0)
            {
                var ids = poll.Rows.Select(x => x.TrnID).ToList();

                DeviceLogger.Log($"DATA RECEIVED | TypeMID={DeviceSession.TypeMID} | {string.Join(",", ids)}");

                DeviceMemory.LastIds = ids;

                await Task.Delay(2000); // simulate device work

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
        AddAuth();

        var payload = new { TrnIDs = ids };

        var res = await _http.PostAsync("poll/ack",
            new StringContent(JsonSerializer.Serialize(payload),
            Encoding.UTF8, "application/json"));

        if (res.IsSuccessStatusCode)
            DeviceLogger.Log($"ACK SENT | TypeMID={DeviceSession.TypeMID} | {string.Join(",", ids)} ");
        else
            DeviceLogger.Log($"ACK FAILED | TypeMID={DeviceSession.TypeMID}");
    }
}