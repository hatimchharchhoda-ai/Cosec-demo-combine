using System.Text.Json;

public static class HttpLogger
{
    public static async Task<(HttpResponseMessage res, string body)> SendAsync(
        HttpClient http,
        HttpRequestMessage req,
        string action)
    {
        

        var reqBody = req.Content == null
            ? "null"
            : await req.Content.ReadAsStringAsync();

        DeviceLogger.Debug(
            $"{action} | REQUEST | {req.Method} {req.RequestUri} | " +
            $"Headers={req.Headers} |  | Body={reqBody}");
        
        var start = DateTime.Now;
        var res = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
        var end  = DateTime.Now;

        if ((int)res.StatusCode == 401 || (int)res.StatusCode == 403)
        {
            DeviceState.SetDisconnected("401/403 from server");
        }

        var body = await res.Content.ReadAsStringAsync();

        DateTime? serverSentAt = ExtractServerSentAt(body);

        var downstreamMs = serverSentAt.HasValue
            ? (end - serverSentAt.Value).TotalMilliseconds
            : -1;

        DeviceLogger.Debug(
            $"{action} | RESPONSE | Status:{(int)res.StatusCode} | " +
            $"Started request:{start:HH:mm:ss.fff} | Response arrived:{end:HH:mm:ss.fff} | " +
            $"FullRoundTrip:{(end - start).TotalMilliseconds} ms | " +
            $"DownstreamMS:{downstreamMs} ms | Body:{body} | Headers:{res.Headers} | ContentHeaders:{res.Content.Headers} | Response:{res}");

        // IMPORTANT: re-attach body because we consumed the stream
        res.Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");

        return (res, body);
    }

    private static DateTime? ExtractServerSentAt(string body)
    {
        try
        {
            var doc = JsonDocument.Parse(body).RootElement;

            if (doc.TryGetProperty("serverSentAt", out var s))
                return s.GetDateTime();

            if (doc.TryGetProperty("ServerSentAt", out var s2))
                return s2.GetDateTime();
        }
        catch { }

        return null;
    }
}