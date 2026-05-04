public static class ConnectionSupervisor
{
    // Back-off ladder in seconds: 5 → 10 → 20 → 60 (then stays at 60)
    private static readonly int[] BackoffSeconds = { 5, 10, 20, 60 };

    private const int FailThreshold = 3;

    public static void Start(
        ApiClient api, DeviceInfo device, DeviceConfig cfg, DeviceLogger logger)
    {
        var label = "SUPERVISOR";
        logger.Info($"{label} | Starting connection supervisor");

        _ = Task.Run(async () =>
        {
            int consecutiveLoginFails = 0;

            while (true)
            {
                try
                {
                    // ── Wait until we detect a problem ────────────────────────
                    // Poll every 3 s so we react quickly when the token-refresh
                    // loop or a poll/event call marks the client disconnected.

                    bool needsReconnect =
                        api.ConsecutiveFailCount >= FailThreshold;

                    if (!needsReconnect)
                    {
                        // Everything is healthy — make sure the flag is clear.
                        if (api.SupervisorActive)
                        {
                            api.MarkSupervisorInactive();
                            logger.Info($"{label} | Connection healthy — supervisor stepping back");
                        }
                        consecutiveLoginFails = 0;
                        continue;
                    }

                    // ── Take over ─────────────────────────────────────────────
                    if (!api.SupervisorActive)
                    {
                        logger.Warn(
                            $"{label} | TAKING OVER | " +
                            $"IsConnected={api.IsConnected} " +
                            $"ConsecutiveFailCount={api.ConsecutiveFailCount} " +
                            $"(threshold={FailThreshold}) — poll/event loops paused");
                        api.MarkSupervisorActive();
                    }

                    int backoff = BackoffSeconds[
                        Math.Min(consecutiveLoginFails, BackoffSeconds.Length - 1)];

                    logger.Info(
                        $"{label} | Reconnecting in {backoff}s " +
                        $"(LoginAttempt={consecutiveLoginFails + 1})");

                    await Task.Delay(TimeSpan.FromSeconds(backoff));

                    logger.Info($"{label} | Attempting re-login...");
                    await api.Login();

                    if (api.IsConnected)
                    {
                        logger.Info(
                            $"{label} | Reconnected successfully " +
                            $"after {consecutiveLoginFails} failed attempt(s) — " +
                            $"resuming poll/event loops");
                        consecutiveLoginFails = 0;
                        api.MarkSupervisorInactive();   // resume loops
                    }
                    else
                    {
                        consecutiveLoginFails++;
                        logger.Warn(
                            $"{label} | Re-login did not restore connection | " +
                            $"ConsecutiveLoginFails={consecutiveLoginFails}");
                        // Stay supervisor-active; loops remain paused.
                    }
                }
                catch (Exception ex)
                {
                    consecutiveLoginFails++;
                    logger.Error(
                        $"{label} | SUPERVISOR-EXCEPTION | " +
                        $"{ex.GetType().Name}: {ex.Message} | " +
                        $"ConsecutiveLoginFails={consecutiveLoginFails}");
                    await Task.Delay(TimeSpan.FromSeconds(5));
                }
            }
        });
    }
}