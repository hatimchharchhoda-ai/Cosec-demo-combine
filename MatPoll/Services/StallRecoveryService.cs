using MatPoll.Repositories;
using MatPoll.Services;

namespace MatPoll.Services;

// Runs every 2 minutes in background.
// Finds rows stuck at TrnStat=1 longer than StallTimeoutMinutes.
// Resets them to TrnStat=0 (or TrnStat=9 if retry >= 5).
// Logs per-device stall groups to error.log so you know which device stopped ACKing.

public class StallRecoveryService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ActivityLogger       _actLog;
    private readonly IConfiguration       _config;

    public StallRecoveryService(
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
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(2), ct);

                var timeoutMins = _config.GetValue<int>(
                    "PollingSettings:StallTimeoutMinutes", 5);

                _actLog.LogTestingStep(
                    "[STALL-RUN] Checking for rows stuck at TrnStat=1 older than {Min} minutes",
                    timeoutMins);

                using var scope = _scopeFactory.CreateScope();
                var repo = scope.ServiceProvider.GetRequiredService<AppRepository>();

                var groups = await repo.ResetStalledRowsAsync(timeoutMins);

                // Log results — writes to error.log if stalled rows found
                _actLog.LogStallRecovery(groups);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                // DB connection failure during stall check → error.log
                _actLog.LogDbFailure("STALL-RECOVERY", "N/A", ex);
            }
        }
    }
}
