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

        ConnectionSupervisor.Start(api, cfg);

        // POLL LOOP
        _ = Task.Run(async () =>
        {
            while (true)
            {
                if (DeviceState.IsConnected)
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
                if (!DeviceState.IsConnected)
                {
                    await Task.Delay(1000);
                    continue;
                }

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
                    if (!DeviceState.IsConnected)
                    {
                        await Task.Delay(1000);
                        continue;
                    }

                    await Task.Delay(cfg.Timing.BulkEverySeconds * 1000);

                    DeviceLogger.Info("BULK EVENT START");

                    var tasks = new List<Task>();

                    for (int i = 0; i < cfg.Event.BulkCount; i++)
                    {
                        tasks.Add(api.SendEventAsync($"Bulk Event {i + 1}"));
                    }

                    await Task.WhenAll(tasks);
                    DeviceLogger.Info("BULK EVENT END");
                }
            });
        }

        await Task.Delay(-1);
    }
}