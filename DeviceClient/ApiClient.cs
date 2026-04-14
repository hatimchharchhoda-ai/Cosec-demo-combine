using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

public class ApiClient
{
    private readonly HttpClient _http;

    public ApiClient()
    {
        _http = new HttpClient();
        _http.BaseAddress = new Uri("https://localhost:58388/api/");
    }

    private void AddHeaders()
    {
        _http.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", DeviceSession.Token);

        _http.DefaultRequestHeaders.Remove("TypeMID");
        _http.DefaultRequestHeaders.Add("TypeMID", DeviceSession.TypeMID.ToString());
    }

    public async Task Login(int deviceId, string mac, string ip)
    {
        var payload = new { DeviceID = deviceId, MACAddr = mac, IPAddr = ip };

        var res = await _http.PostAsync("auth/login",
            new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"));

        var json = await res.Content.ReadAsStringAsync();
        var doc = JsonDocument.Parse(json).RootElement;

        DeviceSession.Token = doc.GetProperty("token").GetString();
        DeviceSession.TypeMID = doc.GetProperty("typeMID").GetInt32();

        DeviceLogger.Log($"LOGIN SUCCESS | TypeMID={DeviceSession.TypeMID}");
    }

    public async Task PollAndProcess()
    {
        try
        {
            AddHeaders();

            var res = await _http.GetAsync("poll");
            var json = await res.Content.ReadAsStringAsync();

            var root = JsonDocument.Parse(json).RootElement;

            bool needAckFirst = root.GetProperty("needAckFirst").GetBoolean();
            bool hasData = root.GetProperty("hasData").GetBoolean();
            string batchToken = root.GetProperty("batchToken").GetString();

            // CASE 1: Server asks ACK first
            if (needAckFirst)
            {
                DeviceLogger.Log($"ACK-FIRST requested | Token={batchToken}");
                await Ack(batchToken, new List<decimal>());
                return;
            }

            // CASE 2: Data received
            if (hasData)
            {
                var rows = root.GetProperty("rows");

                List<decimal> ids = new();

                foreach (var row in rows.EnumerateArray())
                {
                    decimal id = row.GetProperty("trnID").GetDecimal();
                    ids.Add(id);
                }

                DeviceLogger.Log($"DATA RECEIVED | IDs={string.Join(",", ids)}");

                // Simulate device processing
                await Task.Delay(2000);

                await Ack(batchToken, ids);
            }
        }
        catch (Exception ex)
        {
            DeviceLogger.Log($"ERROR | {ex.Message}");
        }
    }

    private async Task Ack(string batchToken, List<decimal> trnIds)
    {
        AddHeaders();

        var payload = new { batchToken, trnIDs = trnIds };

        var res = await _http.PostAsync("poll/ack",
            new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"));

        if (res.IsSuccessStatusCode)
        {
            DeviceLogger.Log($"ACK SENT | IDs={string.Join(",", trnIds)}");
        }
        else
        {
            DeviceLogger.Log($"ACK FAILED | Status={res.StatusCode}");
        }
    }
}