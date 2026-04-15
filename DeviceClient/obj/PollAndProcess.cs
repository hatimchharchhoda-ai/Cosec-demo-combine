public async Task PollAndProcess()
{
    try
    {
        AddHeaders();

        var res  = await _http.GetAsync("poll");
        var json = await res.Content.ReadAsStringAsync();

        var poll = JsonSerializer.Deserialize<PollResponse>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        // CASE 1 — server says ACK first
        if (poll.NeedAckFirst)
        {
            if (DeviceMemory.LastDispatchedIds.Count > 0)
            {
                DeviceLogger.Log("ACK-FIRST received. Sending ACK for previous batch.");
                await Ack(DeviceMemory.LastDispatchedIds);
                DeviceMemory.LastDispatchedIds.Clear();
            }
            return;
        }

        // CASE 2 — new data arrived
        if (poll.HasData && poll.Rows.Count > 0)
        {
            var ids = poll.Rows.Select(x => x.TrnID).ToList();

            DeviceLogger.Log($"DATA RECEIVED | {string.Join(",", ids)}");

            // Store in memory (VERY IMPORTANT)
            DeviceMemory.LastDispatchedIds = ids;

            // simulate device work
            await Task.Delay(2000);

            await Ack(ids);

            DeviceMemory.LastDispatchedIds.Clear();
        }
    }
    catch (Exception ex)
    {
        DeviceLogger.Log($"ERROR | {ex.Message}");
    }
}