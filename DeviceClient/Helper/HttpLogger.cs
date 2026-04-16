public static class HttpLogger
{
    public static async Task<HttpResponseMessage> SendAsync(
        HttpClient http,
        HttpRequestMessage req,
        string action)
    {
        var start = DateTime.Now;

        DeviceLogger.Debug(
            $"{action} | REQUEST | {req.Method} {req.RequestUri}\n" +
            $"Headers={req.Headers}\n" +
            $"Body={(req.Content == null ? "null" : await req.Content.ReadAsStringAsync())}");

        var res = await http.SendAsync(req);

        var end = DateTime.Now;
        var body = await res.Content.ReadAsStringAsync();

        DeviceLogger.Debug(
            $"{action} | RESPONSE | Status={(int)res.StatusCode}\n" +
            $"DurationMs={(end-start).TotalMilliseconds}\n" +
            $"Body={body}");

        return res;
    }
}