public class DeviceSimulator
{
    private readonly DeviceConfig _cfg;
    private readonly ApiClient    _api;
    private readonly DeviceLogger _logger;
    private readonly string       _label;

    public DeviceSimulator(DeviceInfo device, DeviceConfig cfg)
    {
        _cfg    = cfg;
        _logger = new DeviceLogger(device, cfg.Logging);   // ← create here
        _api    = new ApiClient(cfg.Server.BaseUrl, device, _logger);  // ← pass in
        _label  = $"[{device.MACAddr}]";

        if (cfg.Timing.PollIntervalSeconds  <= 0) _logger.Warn($"{_label} INIT | PollIntervalSeconds={cfg.Timing.PollIntervalSeconds} is invalid (<=0)");
        if (cfg.Timing.EventIntervalSeconds <= 0) _logger.Warn($"{_label} INIT | EventIntervalSeconds={cfg.Timing.EventIntervalSeconds} is invalid (<=0)");
        if (cfg.Event.EventCount            <= 0) _logger.Warn($"{_label} INIT | EventCount={cfg.Event.EventCount} is invalid (<=0)");
    }

    public async Task RunAsync(CancellationToken ct)
    {
        var jitter = Random.Shared.Next(0, 8000);
        _logger.Debug($"{_label} RUN | Jitter delay={jitter}ms before login");
        await Task.Delay(jitter, ct);

        _logger.Info($"{_label} RUN | Initial login starting");
        await _api.Login();

        if (!_api.IsConnected)
            _logger.Warn($"{_label} RUN | Initial login failed — supervisor will retry; loops starting anyway");

        // Start supervisor regardless — it will reconnect as needed
        var device = _cfg.Devices.FirstOrDefault(d => d.MACAddr == _label.Trim('[', ']')) ?? new DeviceInfo();
        ConnectionSupervisor.Start(_api, device, _cfg, _logger);

        _logger.Info($"{_label} RUN | Starting all loops");

        try
        {
            await Task.WhenAll(
                RunPollLoop(ct),
                RunEventLoop(ct)
            );
        }
        catch (OperationCanceledException)
        {
            _logger.Info($"{_label} RUN | Cancelled gracefully");
        }
        catch (Exception ex)
        {
            _logger.Error($"{_label} RUN | UNHANDLED EXCEPTION | {ex.GetType().Name}: {ex.Message}");
            throw; // let Program.cs observe it
        }

        _logger.Info($"{_label} RUN | All loops exited");
    }

    // ── Loops ──────────────────────────────────────────────────────────────────

    private async Task RunPollLoop(CancellationToken ct)
    {
        var ctx = $"{_label} POLL-LOOP";
        _logger.Debug($"{ctx} | Started | Interval={_cfg.Timing.PollIntervalSeconds}s");

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
                    _logger.Debug($"{ctx} | Skipped — not connected");
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"{ctx} | EXCEPTION | {ex.GetType().Name}: {ex.Message}");
            }

            try
            {
                await Task.Delay(_cfg.Timing.PollIntervalSeconds * 1000, ct);
            }
            catch (OperationCanceledException) { break; }
        }

        _logger.Debug($"{ctx} | Stopped");
    }

    private async Task RunEventLoop(CancellationToken ct)
    {
        var ctx = $"{_label} EVENT-LOOP";
        int counter = 1;
        int intervalCounter = 0;

        _logger.Debug($"{ctx} | Started | Interval={_cfg.Timing.EventIntervalSeconds}s EventCount={_cfg.Event.EventCount}");

        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (!_api.IsConnected)
                {
                    _logger.Debug($"{ctx} | Skipped — not connected");
                    await Task.Delay(1000, ct);
                    continue;
                }

                intervalCounter++;

                // Every 5th interval → 10x load
                int eventsThisRound = (intervalCounter % 5 == 0)
                    ? _cfg.Event.EventCount * 10
                    : _cfg.Event.EventCount;

                _logger.Debug($"{ctx} | Interval={intervalCounter} | Sending {eventsThisRound} events");

                for (int i = 0; i < eventsThisRound; i++)
                {
                    var msg = $"Heartbeat #{counter++}";
                    await _api.SendEventAsync(msg, counter - 1);
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"{ctx} | EXCEPTION | {ex.GetType().Name}: {ex.Message}");
            }

            try
            {
                await Task.Delay(_cfg.Timing.EventIntervalSeconds * 1000, ct);
            }
            catch (OperationCanceledException) { break; }
        }

        _logger.Debug($"{ctx} | Stopped | LastCounter={counter}");
    }
}