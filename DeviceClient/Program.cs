class Program
{
    static async Task Main()
    {
        // ── Catch anything that leaks past Task.WhenAll ────────────────────────
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            var ex = e.ExceptionObject as Exception;
            DeviceLogger.Error($"[PROGRAM] UNHANDLED-EXCEPTION | IsTerminating={e.IsTerminating} | {ex?.GetType().Name}: {ex?.Message}");
            Console.Error.WriteLine($"[FATAL] {ex}");
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            DeviceLogger.Error($"[PROGRAM] UNOBSERVED-TASK-EXCEPTION | {e.Exception?.GetType().Name}: {e.Exception?.Message}");
            e.SetObserved(); // prevent process crash
        };

        // ── Load config ────────────────────────────────────────────────────────
        DeviceConfig cfg;
        try
        {
            cfg = DeviceConfigLoader.Load();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[FATAL] Failed to load config: {ex.Message}");
            Environment.Exit(1);
            return;
        }

        // ── Init logger ────────────────────────────────────────────────────────
        DeviceLogger.Configure(
            cfg.Logging.InfoFile,
            cfg.Logging.DebugFile,
            cfg.Logging.ErrorFile,
            cfg.Logging.WarnFile,
            cfg.Logging.EnableInfo,
            cfg.Logging.EnableDebug,
            cfg.Logging.EnableError,
            cfg.Logging.EnableWarn
        );

        // ── Validate config ────────────────────────────────────────────────────
        if (cfg.Devices == null || cfg.Devices.Count == 0)
        {
            DeviceLogger.Error("[PROGRAM] No devices found in config — nothing to simulate. Exiting.");
            Console.Error.WriteLine("[FATAL] No devices configured.");
            Environment.Exit(1);
            return;
        }

        if (string.IsNullOrWhiteSpace(cfg.Server.BaseUrl))
            DeviceLogger.Error("[PROGRAM] Server.BaseUrl is null/empty — all devices will fail to connect");

        // Warn on duplicate MACs
        var duplicateMacs = cfg.Devices
            .GroupBy(d => d.MACAddr)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        if (duplicateMacs.Any())
            DeviceLogger.Warn($"[PROGRAM] Duplicate MAC addresses detected: {string.Join(", ", duplicateMacs)}");

        DeviceLogger.Info($"[PROGRAM] Starting {cfg.Devices.Count} device simulator(s) | Server={cfg.Server.BaseUrl}");

        // ── Cancellation ───────────────────────────────────────────────────────
        var cts = new CancellationTokenSource();

        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            DeviceLogger.Info("[PROGRAM] Ctrl+C received — initiating graceful shutdown...");
            cts.Cancel();
        };

        // Also handle SIGTERM (e.g., Docker stop)
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            if (!cts.IsCancellationRequested)
            {
                DeviceLogger.Info("[PROGRAM] ProcessExit signal — initiating graceful shutdown...");
                cts.Cancel();
            }
        };

        // ── Launch simulators ──────────────────────────────────────────────────
        var tasks = cfg.Devices
            .Select(device =>
            {
                DeviceLogger.Info($"[PROGRAM] Launching simulator for MAC={device.MACAddr} IP={device.IPAddr} Type={device.DeviceType}");
                return new DeviceSimulator(device, cfg).RunAsync(cts.Token);
            })
            .ToList();

        DeviceLogger.Info($"[PROGRAM] All {tasks.Count} device(s) started");

        // ── Await all, collect individual failures ─────────────────────────────
        try
        {
            await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException)
        {
            DeviceLogger.Info("[PROGRAM] Shutdown complete (cancelled)");
        }
        catch (Exception ex)
        {
            DeviceLogger.Error($"[PROGRAM] One or more simulators exited with error | {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            // Report any per-task failures individually
            for (int i = 0; i < tasks.Count; i++)
            {
                var t = tasks[i];
                if (t.IsFaulted)
                {
                    var mac = cfg.Devices[i].MACAddr;
                    DeviceLogger.Error($"[PROGRAM] Device [{mac}] task FAULTED | {t.Exception?.InnerException?.GetType().Name}: {t.Exception?.InnerException?.Message}");
                }
                else if (t.IsCanceled)
                {
                    DeviceLogger.Info($"[PROGRAM] Device [{cfg.Devices[i].MACAddr}] task cancelled normally");
                }
            }

            DeviceLogger.Info("[PROGRAM] Exiting.");
        }
    }
}