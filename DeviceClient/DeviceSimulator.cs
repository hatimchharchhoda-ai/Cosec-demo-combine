public class DeviceSimulator
{
    private readonly DeviceConfig _cfg;
    private readonly ApiClient    _api;
    private readonly DeviceLogger _logger;
    private readonly string       _label;

    public DeviceSimulator(DeviceInfo device, DeviceConfig cfg)
    {
        _cfg    = cfg;
        _logger = new DeviceLogger(device, cfg.Logging);
        _api    = new ApiClient(cfg.Server.BaseUrl, device, _logger);
        _label  = $"[{device.MACAddr}]";

        if (cfg.Timing.PollIntervalSeconds  <= 0)
            _logger.Warn($"{_label} INIT | PollIntervalSeconds={cfg.Timing.PollIntervalSeconds} is invalid (<=0)");
        if (cfg.Timing.EventIntervalSeconds <= 0)
            _logger.Warn($"{_label} INIT | EventIntervalSeconds={cfg.Timing.EventIntervalSeconds} is invalid (<=0)");
        if (cfg.Event.EventCount            <= 0)
            _logger.Warn($"{_label} INIT | EventCount={cfg.Event.EventCount} is invalid (<=0)");
    }

    public async Task RunAsync(CancellationToken ct)
    {
        var jitter = Random.Shared.Next(0, 8000);
        _logger.Debug($"RUN | Jitter delay={jitter}ms before login");
        await Task.Delay(jitter, ct);

        _logger.Info("RUN | Initial login starting");
        await _api.Login();

        if (!_api.IsConnected)
            _logger.Warn("RUN | Initial login failed — supervisor will retry; loops starting anyway");

        // Resolve the DeviceInfo for this simulator to pass to the supervisor.
        var device = _cfg.Devices.FirstOrDefault(d => d.MACAddr == _label.Trim('[', ']'))
                     ?? new DeviceInfo();
        ConnectionSupervisor.Start(_api, device, _cfg, _logger);

        _logger.Info("RUN | Starting all loops");

        try
        {
            await Task.WhenAll(
                RunPollLoop(ct),
                RunEventLoop(ct)
            );
        }
        catch (OperationCanceledException)
        {
            _logger.Info("RUN | Cancelled gracefully");
        }
        catch (Exception ex)
        {
            _logger.Error($"RUN | UNHANDLED EXCEPTION | {ex.GetType().Name}: {ex.Message}");
            throw;
        }

        _logger.Info("RUN | All loops exited");
    }

    // ── Loops ──────────────────────────────────────────────────────────────────

    private async Task RunPollLoop(CancellationToken ct)
    {
        var ctx = "POLL-LOOP";
        _logger.Debug($"{ctx} | Started | Interval={_cfg.Timing.PollIntervalSeconds}s");

        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (_api.SupervisorActive)
                {
                    // Supervisor is reconnecting — stay completely silent.
                    // Just sleep for a short period and check again.
                    await Task.Delay(TimeSpan.FromSeconds(1), ct);
                    continue;
                }

                if (_api.IsConnected)
                {
                    await _api.PollAndProcess();
                }
                else
                {
                    // Not connected but supervisor hasn't engaged yet
                    // (fail count < 3).  PollAndProcess handles the 2 s wait
                    // and the fail-count increment internally.
                    await _api.PollAndProcess();
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
        var ctx = "EVENT-LOOP";
        int counter         = 1;
        int intervalCounter = 0;

        _logger.Debug(
            $"{ctx} | Started | Interval={_cfg.Timing.EventIntervalSeconds}s " +
            $"EventCount={_cfg.Event.EventCount}");

        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (_api.SupervisorActive)
                {
                    // Supervisor is reconnecting — stay completely silent.
                    await Task.Delay(TimeSpan.FromSeconds(1), ct);
                    continue;
                }

                if (!_api.IsConnected)
                {
                    // Not yet at threshold — SendEventAsync handles the 2 s
                    // wait and fail-count increment internally.
                    await _api.SendEventAsync($"Heartbeat #{counter}", counter);
                    counter++;
                    await Task.Delay(_cfg.Timing.EventIntervalSeconds * 1000, ct);
                    continue;
                }

                intervalCounter++;

                // Every 5th interval → 10x load burst.
                int eventsThisRound = (intervalCounter % 5 == 0)
                    ? _cfg.Event.EventCount * 10
                    : _cfg.Event.EventCount;

                _logger.Debug(
                    $"{ctx} | Interval={intervalCounter} | Sending {eventsThisRound} event(s)");

                for (int i = 0; i < eventsThisRound && !ct.IsCancellationRequested; i++)
                {
                    // Re-check supervisor flag inside the burst so we stop
                    // mid-burst if the supervisor engages.
                    if (_api.SupervisorActive) break;

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