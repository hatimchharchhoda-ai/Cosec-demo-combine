using System.Text.Json;

public static class HttpLogger
{
    public static async Task<(HttpResponseMessage res, string body)> SendAsync(
        HttpClient http,
        HttpRequestMessage req,
        string action,
        DeviceLogger logger)           // per-device logger injected here
    {
        // ── Read request body before sending (stream is single-use) ───────────
        string reqBody = "(no content)";
        if (req.Content != null)
        {
            try
            {
                reqBody = await req.Content.ReadAsStringAsync();
                req.Content = new StringContent(reqBody,
                    System.Text.Encoding.UTF8,
                    req.Content.Headers.ContentType?.MediaType ?? "application/json");
            }
            catch (Exception ex)
            {
                reqBody = $"(unreadable: {ex.Message})";
                logger.Warn($"{action} | REQUEST | Could not read request body: {ex.Message}");
            }
        }

        logger.Debug(
            $"{action} | REQUEST | Requested for url from server: {req.Method} {req.RequestUri} | Sending Content to server={Truncate(reqBody, 1000)}");

        // ── Send ──────────────────────────────────────────────────────────────
        var start = DateTime.UtcNow;
        HttpResponseMessage res;

        try
        {
            res = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
        }
        catch (TaskCanceledException tcex)
        {
            logger.Error($"{action} | SEND-TIMEOUT | {tcex.Message} | URI={req.RequestUri}");
            throw;
        }
        catch (HttpRequestException hre)
        {
            logger.Error($"{action} | SEND-FAILED | {hre.Message} | URI={req.RequestUri}");
            throw;
        }

        var end = DateTime.UtcNow;

        // ── Read response body ────────────────────────────────────────────────
        string body;
        try
        {
            body = await res.Content.ReadAsStringAsync();
        }
        catch (Exception ex)
        {
            body = string.Empty;
            logger.Error($"{action} | RESPONSE-READ-FAILED | Status={(int)res.StatusCode} | {ex.Message}");
        }

        // ── Auth failures ──────────────────────────────────────────────────────
        var statusCode = (int)res.StatusCode;
        if (statusCode == 401 || statusCode == 403)
            logger.Warn($"{action} | AUTH-FAILURE | Status={statusCode} | URI={req.RequestUri} | Body={Truncate(body, 300)}");

        // ── Latency ───────────────────────────────────────────────────────────
        DateTime? serverSentAt = ExtractServerSentAt(body);
        double downstreamMs    = serverSentAt.HasValue ? (end - serverSentAt.Value).TotalMilliseconds : -1;
        double roundTripMs     = (end - start).TotalMilliseconds;

        if (roundTripMs > 5000)
            logger.Warn($"{action} | SLOW-RESPONSE | RoundTrip={roundTripMs:F0}ms URI={req.RequestUri}");

        if (downstreamMs > 0 && downstreamMs > 3000)
            logger.Warn($"{action} | SLOW-DOWNSTREAM | Downstream={downstreamMs:F0}ms");

        logger.Debug(
            $"{action} | RESPONSE | Status={statusCode} | " +
            $"Total Time={roundTripMs}ms | " +
            $"Content get from Server={Truncate(body, 1000)}");

        if (res.IsSuccessStatusCode && string.IsNullOrWhiteSpace(body))
            logger.Warn($"{action} | EMPTY-SUCCESS-BODY | Status={statusCode} | URI={req.RequestUri}");

        // ── Re-attach body for downstream consumers ───────────────────────────
        res.Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");

        return (res, body);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static DateTime? ExtractServerSentAt(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        try
        {
            var doc = JsonDocument.Parse(body).RootElement;
            if (doc.TryGetProperty("serverSentAt", out var s) && s.ValueKind == JsonValueKind.String)
                return s.GetDateTime();
            if (doc.TryGetProperty("ServerSentAt", out var s2) && s2.ValueKind == JsonValueKind.String)
                return s2.GetDateTime();
        }
        catch { }
        return null;
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + $"…[+{s.Length - max} chars]";
}