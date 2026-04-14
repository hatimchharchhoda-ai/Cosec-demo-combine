using MatPoll.Repositories;

namespace MatPoll.Services;

// Runs automatically in background every 2 minutes.
// Finds rows stuck at TrnStat=1 (dispatched but never ACKed)
// and resets them back to TrnStat=0 so they get sent again next poll.
// After 5 failed retries → TrnStat=9 (permanently failed).

public class StallRecoveryService : BackgroundService
{
    private readonly IServiceScopeFactory         _scopeFactory;
    private readonly ILogger<StallRecoveryService> _logger;

    public StallRecoveryService(
        IServiceScopeFactory scopeFactory,
        ILogger<StallRecoveryService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger       = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                // Wait 2 minutes between each run
                await Task.Delay(TimeSpan.FromMinutes(2), ct);

                // Create a fresh scope to get a new DbContext
                using var scope = _scopeFactory.CreateScope();
                var repo = scope.ServiceProvider.GetRequiredService<AppRepository>();

                await repo.ResetStalledRowsAsync(timeoutMinutes: 5);

                _logger.LogInformation("[StallRecovery] Ran at {Time}", DateTime.Now);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[StallRecovery] Error");
            }
        }
    }
}