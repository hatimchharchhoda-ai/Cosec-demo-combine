public class DeviceSimulator
{
    private readonly DeviceConfig _cfg;
    private readonly ApiClient    _api;
    private readonly string       _label;

    public DeviceSimulator(DeviceInfo device, DeviceConfig cfg)
    {
        _cfg   = cfg;
        _api   = new ApiClient(cfg.Server.BaseUrl, device);
        _label = $"[{device.MACAddr}]";

        // Validate config fields at construction time
        if (cfg.Timing.PollIntervalSeconds  <= 0) DeviceLogger.Warn($"{_label} INIT | PollIntervalSeconds={cfg.Timing.PollIntervalSeconds} is invalid (<=0)");
        if (cfg.Timing.EventIntervalSeconds <= 0) DeviceLogger.Warn($"{_label} INIT | EventIntervalSeconds={cfg.Timing.EventIntervalSeconds} is invalid (<=0)");
        if (cfg.Event.EventCount            <= 0) DeviceLogger.Warn($"{_label} INIT | EventCount={cfg.Event.EventCount} is invalid (<=0)");
    }

    public async Task RunAsync(CancellationToken ct)
    {
        var jitter = Random.Shared.Next(0, 8000);
        DeviceLogger.Debug($"{_label} RUN | Jitter delay={jitter}ms before login");
        await Task.Delay(jitter, ct);

        DeviceLogger.Info($"{_label} RUN | Initial login starting");
        await _api.Login();

        if (!_api.IsConnected)
            DeviceLogger.Warn($"{_label} RUN | Initial login failed — supervisor will retry; loops starting anyway");

        // Start supervisor regardless — it will reconnect as needed
        ConnectionSupervisor.Start(_api, _cfg.Devices.FirstOrDefault(d => d.MACAddr == _label.Trim('[', ']')) ?? new DeviceInfo(), _cfg);

        DeviceLogger.Info($"{_label} RUN | Starting all loops");

        try
        {
            await Task.WhenAll(
                RunPollLoop(ct),
                RunEventLoop(ct),
                RunBulkLoop(ct)
            );
        }
        catch (OperationCanceledException)
        {
            DeviceLogger.Info($"{_label} RUN | Cancelled gracefully");
        }
        catch (Exception ex)
        {
            DeviceLogger.Error($"{_label} RUN | UNHANDLED EXCEPTION | {ex.GetType().Name}: {ex.Message}");
            throw; // let Program.cs observe it
        }

        DeviceLogger.Info($"{_label} RUN | All loops exited");
    }

    // ── Loops ──────────────────────────────────────────────────────────────────

    private async Task RunPollLoop(CancellationToken ct)
    {
        var ctx = $"{_label} POLL-LOOP";
        DeviceLogger.Debug($"{ctx} | Started | Interval={_cfg.Timing.PollIntervalSeconds}s");

        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (_api.IsConnected)
                {
                    await _api.PollAndProcess();
                }
                else
                {
                    DeviceLogger.Debug($"{ctx} | Skipped — not connected");
                }
            }
            catch (Exception ex)
            {
                DeviceLogger.Error($"{ctx} | EXCEPTION | {ex.GetType().Name}: {ex.Message}");
            }

            try
            {
                await Task.Delay(_cfg.Timing.PollIntervalSeconds * 1000, ct);
            }
            catch (OperationCanceledException) { break; }
        }

        DeviceLogger.Debug($"{ctx} | Stopped");
    }

    private async Task RunEventLoop(CancellationToken ct)
    {
        var ctx     = $"{_label} EVENT-LOOP";
        int counter = 1;
        DeviceLogger.Debug($"{ctx} | Started | Interval={_cfg.Timing.EventIntervalSeconds}s EventCount={_cfg.Event.EventCount}");

        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (!_api.IsConnected)
                {
                    DeviceLogger.Debug($"{ctx} | Skipped — not connected");
                    try { await Task.Delay(1000, ct); } catch (OperationCanceledException) { break; }
                    continue;
                }

                var messages = Enumerable
                    .Range(counter, _cfg.Event.EventCount)
                    .Select(i => $"Heartbeat #{i}")
                    .ToList();

                DeviceLogger.Debug($"{ctx} | Sending {messages.Count} events starting at counter={counter}");

                counter += _cfg.Event.EventCount;
                await _api.SendBulkEventsAsync(messages);
            }
            catch (Exception ex)
            {
                DeviceLogger.Error($"{ctx} | EXCEPTION | {ex.GetType().Name}: {ex.Message}");
            }

            try
            {
                await Task.Delay(_cfg.Timing.EventIntervalSeconds * 1000, ct);
            }
            catch (OperationCanceledException) { break; }
        }

        DeviceLogger.Debug($"{ctx} | Stopped | LastCounter={counter}");
    }

    private async Task RunBulkLoop(CancellationToken ct)
    {
        var ctx = $"{_label} BULK-LOOP";

        if (!_cfg.Event.EnableBulkMode)
        {
            DeviceLogger.Debug($"{ctx} | Disabled in config — exiting");
            return;
        }

        DeviceLogger.Debug($"{ctx} | Started | Every={_cfg.Timing.BulkEverySeconds}s BulkCount={_cfg.Event.BulkCount}");

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_cfg.Timing.BulkEverySeconds * 1000, ct);
            }
            catch (OperationCanceledException) { break; }

            try
            {
                if (!_api.IsConnected)
                {
                    DeviceLogger.Debug($"{ctx} | Skipped — not connected");
                    continue;
                }

                if (_cfg.Event.BulkCount <= 0)
                {
                    DeviceLogger.Warn($"{ctx} | BulkCount={_cfg.Event.BulkCount} is invalid — skipping");
                    continue;
                }

                var messages = Enumerable
                    .Range(1, _cfg.Event.BulkCount)
                    .Select(i => $"Bulk Event {i}")
                    .ToList();

                DeviceLogger.Debug($"{ctx} | Sending {messages.Count} bulk events");
                await _api.SendBulkEventsAsync(messages);
            }
            catch (Exception ex)
            {
                DeviceLogger.Error($"{ctx} | EXCEPTION | {ex.GetType().Name}: {ex.Message}");
            }
        }

        DeviceLogger.Debug($"{ctx} | Stopped");
    }
}