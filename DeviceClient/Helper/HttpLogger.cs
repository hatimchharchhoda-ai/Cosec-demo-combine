public static class HttpLogger
{
    public static async Task<HttpResponseMessage> SendAsync(
        HttpClient http,
        HttpRequestMessage req,
        string action)
    {
        var start = DateTime.Now;

        DeviceLogger.Debug(
            $"{action} | REQUEST | {req.Method} {req.RequestUri} | " +
            $"Headers={req.Headers} | Start={start} | " +
            $"Body={(req.Content == null ? "null" : await req.Content.ReadAsStringAsync())}");

        var res = await http.SendAsync(req);

        var end = DateTime.Now;
        var body = await res.Content.ReadAsStringAsync();

        DeviceLogger.Debug(
            $"{action} | RESPONSE | Status={(int)res.StatusCode} | " +
            $"Started request={start} | Response arrived={end} | FullRoundTrip={(end-start).TotalMilliseconds} ms | DownstreamMS: {(end - res.ServerSentAt)?.TotalMilliseconds} ms | Body={body}");

        return res;
    }
}