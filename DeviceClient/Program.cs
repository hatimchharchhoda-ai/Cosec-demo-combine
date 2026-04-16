class Program
{
    static async Task Main()
    {
        var cfg = DeviceConfigLoader.Load();

        DeviceLogger.Configure(
            cfg.Logging.InfoFile,
            cfg.Logging.DebugFile,
            cfg.Logging.ErrorFile,
            cfg.Logging.EnableInfo,
            cfg.Logging.EnableDebug,
            cfg.Logging.EnableError
        );

        DeviceLogger.Info("LOGGER INITIALIZED");

        var api = new ApiClient(cfg.Server.BaseUrl);

        await api.Login(
            cfg.Device.DeviceId,
            cfg.Device.MacAddress,
            cfg.Device.IpAddress);

        await api.Restore();

        // POLL LOOP
        _ = Task.Run(async () =>
        {
            while (true)
            {
                await api.PollAndProcess();
                await Task.Delay(cfg.Timing.PollIntervalSeconds * 1000);
            }
        });

        // EVENT LOOP
        _ = Task.Run(async () =>
        {
            int counter = 1;

            while (true)
            {
                DeviceLogger.Info("NORMAL EVENT BATCH START");
                for (int i = 0; i < cfg.Event.EventCount; i++)
                {
                    await api.SendEventAsync($"Heartbeat #{counter++}");
                }
                DeviceLogger.Info("NORMAL EVENT BATCH END");
                await Task.Delay(cfg.Timing.EventIntervalSeconds * 1000);
            }
        });

        // BULK MODE LOOP
        if (cfg.Event.EnableBulkMode)
        {
            _ = Task.Run(async () =>
            {
                while (true)
                {
                    await Task.Delay(cfg.Timing.BulkEverySeconds * 1000);

                    DeviceLogger.Info("BULK EVENT START");

                    for (int i = 0; i < cfg.Event.BulkCount; i++)
                    {
                        await api.SendEventAsync($"Bulk Event {i + 1}");
                    }

                    DeviceLogger.Info("BULK EVENT END");
                }
            });
        }

        await Task.Delay(-1);
    }
}