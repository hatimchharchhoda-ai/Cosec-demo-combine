using MatPoll.Repositories;
using MatPoll.Services;

namespace MatPoll.Services;

// ─────────────────────────────────────────────────────────────────────────────
// CommTrnRefillService
//
// Background job that runs every N hours
// Checks each active device's pending row count
// If below threshold → creates new rows directly in DB
// No HTTP call needed — same DB as MatPoll
// ─────────────────────────────────────────────────────────────────────────────

public class CommTrnRefillService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ActivityLogger       _actLog;
    private readonly IConfiguration       _config;

    public CommTrnRefillService(
        IServiceScopeFactory scopeFactory,
        ActivityLogger       actLog,
        IConfiguration       config)
    {
        _scopeFactory = scopeFactory;
        _actLog       = actLog;
        _config       = config;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // wait 1 minute after app starts before first run
        // gives server time to fully initialize
        await Task.Delay(TimeSpan.FromMinutes(1), ct);

        _actLog.LogTestingStep("[REFILL-SERVICE] Started — waiting for first run");

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await RefillAsync(ct);
            }
            catch (OperationCanceledException)
            {
                // normal shutdown
                break;
            }
            catch (Exception ex)
            {
                _actLog.LogDbFailure("COMMTRN-REFILL", ex);
            }

            // wait configured interval before next run
            var intervalHours = _config.GetValue<int>(
                "PollingSettings:CommTrnRefillIntervalHours", 1);

            _actLog.LogTestingStep(
                "[REFILL-SERVICE] Next run in {Hours} hour(s)", intervalHours);

            await Task.Delay(TimeSpan.FromMinutes(intervalHours), ct);
        }

        _actLog.LogTestingStep("[REFILL-SERVICE] Stopped");
    }

    private async Task RefillAsync(CancellationToken ct)
    {
        // create new scope — required for scoped services like AppRepository
        using var scope = _scopeFactory.CreateScope();
        var repo        = scope.ServiceProvider
                              .GetRequiredService<AppRepository>();

        // read config
        var threshold   = _config.GetValue<int>(
            "PollingSettings:CommTrnRefillThreshold", 5);
        var rowsPerFill = _config.GetValue<int>(
            "PollingSettings:CommTrnRowsPerRefill", 100);

        // get all active devices from Mat_DeviceMst
        var devices   = await repo.GetActiveDevicesAsync();
        var startTime = DateTime.UtcNow;
        var totalRows = 0;
        var skipped   = 0;

        _actLog.LogTestingStep(
            "[REFILL-START] ActiveDevices:{Count}  Threshold:{Threshold}  RowsPerFill:{Rows}",
            devices.Count, threshold, rowsPerFill);

        foreach (var device in devices)
        {
            // stop if cancellation requested
            if (ct.IsCancellationRequested) break;

            try
            {
                // check how many pending rows this device has right now
                var pending = await repo.CountPendingAsync(
                    device.DeviceID,
                    device.DeviceType ?? 0);

                // skip if already has enough rows
                if (pending >= threshold)
                {
                    skipped++;
                    _actLog.LogTestingStep(
                        "[REFILL-SKIP] DeviceID:{DeviceID}  Name:{Name}  Pending:{Pending}  Threshold:{Threshold}",
                        device.DeviceID, device.DeviceName ?? "?", pending, threshold);
                    continue;
                }

                // calculate how many to create
                // fill up to rowsPerFill
                var toCreate = rowsPerFill - pending;

                // ── DIRECT DB CALL — no HTTP, no port, no URL ─────────────
                // AppRepository.CreateCommTrnRowsAsync writes directly
                // to Mat_CommTrn using same DbContext as rest of app
                var created = await repo.CreateCommTrnRowsAsync(
                    device.DeviceID,
                    device.DeviceType ?? 0,
                    toCreate);

                totalRows += created;

                _actLog.LogTestingStep(
                    "[REFILL-DONE] DeviceID:{DeviceID}  Name:{Name}  Created:{Created}  WasPending:{Pending}  NowPending:{Now}",
                    device.DeviceID, device.DeviceName ?? "?",
                    created, pending, pending + created);
            }
            catch (Exception ex)
            {
                // log per-device failure but continue with other devices
                _actLog.LogDbFailure(
                    $"COMMTRN-REFILL-Device:{device.DeviceID}", ex);
            }
        }

        // log overall summary
        _actLog.LogRefill(devices.Count, totalRows,  startTime);
    }
}