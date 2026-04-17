using System.Text.Json;

public static class HttpLogger
{
    public static async Task<HttpResponseMessage> SendAsync(
        HttpClient http,
        HttpRequestMessage req,
        string action)
    {
        var start = DateTime.Now;

        var reqBody = req.Content == null
            ? "null"
            : await req.Content.ReadAsStringAsync();

        DeviceLogger.Debug(
            $"{action} | REQUEST | {req.Method} {req.RequestUri} | " +
            $"Headers={req.Headers} | Start={start} | Body={reqBody}");

        var res = await http.SendAsync(req);

        var end  = DateTime.Now;
        var body = await res.Content.ReadAsStringAsync();

        DateTime? serverSentAt = ExtractServerSentAt(body);

        var downstreamMs = serverSentAt.HasValue
            ? (end - serverSentAt.Value).TotalMilliseconds
            : -1;

        DeviceLogger.Debug(
            $"{action} | RESPONSE | Status={(int)res.StatusCode} | " +
            $"Started request={start} | Response arrived={end} | " +
            $"FullRoundTrip={(end - start).TotalMilliseconds} ms | " +
            $"DownstreamMS={downstreamMs} ms | Body={body}");

        // IMPORTANT: re-attach body because we consumed the stream
        res.Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");

        return res;
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