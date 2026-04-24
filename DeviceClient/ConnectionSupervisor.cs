public static class ConnectionSupervisor
{
    // Back-off ladder in seconds: 5 → 10 → 20 → 30 → 60 (then stays at 60)
    private static readonly int[] BackoffSeconds = { 5, 10, 20, 30, 60 };

    public static void Start(ApiClient api, DeviceInfo device, DeviceConfig cfg, DeviceLogger logger)
    {
        var label = $"[{device.MACAddr}] SUPERVISOR";
        logger.Info($"{label} | Starting connection supervisor");

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

                        logger.Info($"{label} | Disconnected — reconnecting in {backoff}s " +
                                    $"(ConsecutiveFails={consecutiveFails})");

                        await Task.Delay(TimeSpan.FromSeconds(backoff));

                        logger.Info($"{label} | Attempting re-login...");
                        await api.Login();

                        if (api.IsConnected)
                        {
                            logger.Info($"{label} | Reconnected successfully after {consecutiveFails} fail(s)");
                            consecutiveFails = 0;
                        }
                        else
                        {
                            consecutiveFails++;
                            logger.Warn($"{label} | Re-login did not restore connection | ConsecutiveFails={consecutiveFails}");
                        }
                    }
                    else
                    {
                        consecutiveFails = 0;
                        // Check every 3 s so we react quickly when TokenRefreshLoop marks disconnected
                        await Task.Delay(TimeSpan.FromSeconds(3));
                    }
                }
                catch (Exception ex)
                {
                    consecutiveFails++;
                    logger.Error($"{label} | SUPERVISOR-EXCEPTION | {ex.GetType().Name}: {ex.Message} | ConsecutiveFails={consecutiveFails}");
                    await Task.Delay(TimeSpan.FromSeconds(5));
                }
            }
        });
    }
}