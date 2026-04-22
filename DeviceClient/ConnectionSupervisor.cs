public static class ConnectionSupervisor
{
    // Back-off ladder in seconds: 5 → 10 → 20 → 30 → 60 (then stays at 60)
    private static readonly int[] BackoffSeconds = { 5, 10, 20, 30, 60 };

    public static void Start(ApiClient api, DeviceInfo device, DeviceConfig cfg)
    {
        var label = $"[{device.MACAddr}] SUPERVISOR";
        DeviceLogger.Info($"{label} | Starting connection supervisor");

        _ = Task.Run(async () =>
        {
            int consecutiveFails = 0;

            while (true)
            {
                try
                {
                    if (!api.IsConnected)
                    {
                        int backoff = BackoffSeconds[Math.Min(consecutiveFails, BackoffSeconds.Length - 1)];

                        DeviceLogger.Info($"{label} | Disconnected — reconnecting in {backoff}s " +
                                          $"(ConsecutiveFails={consecutiveFails})");

                        await Task.Delay(TimeSpan.FromSeconds(backoff));

                        DeviceLogger.Info($"{label} | Attempting re-login...");
                        await api.Login();

                        if (api.IsConnected)
                        {
                            DeviceLogger.Info($"{label} | Reconnected successfully after {consecutiveFails} fail(s)");
                            consecutiveFails = 0;
                        }
                        else
                        {
                            consecutiveFails++;
                            DeviceLogger.Warn($"{label} | Re-login did not restore connection | ConsecutiveFails={consecutiveFails}");
                        }
                    }
                    else
                    {
                        consecutiveFails = 0;
                        // Poll supervisor health check every 30s when connected
                        await Task.Delay(TimeSpan.FromSeconds(30));
                    }
                }
                catch (Exception ex)
                {
                    consecutiveFails++;
                    DeviceLogger.Error($"{label} | SUPERVISOR-EXCEPTION | {ex.GetType().Name}: {ex.Message} | ConsecutiveFails={consecutiveFails}");

                    // Safety delay to prevent tight exception loop
                    await Task.Delay(TimeSpan.FromSeconds(5));
                }
            }
            // ReSharper disable once FunctionNeverReturns
        });
    }
}