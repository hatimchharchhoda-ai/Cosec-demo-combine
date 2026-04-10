using MatGenServer.Repositories.Interfaces;

namespace MatGenServer.Services
{ }

/// <summary>
/// Runs every 2 minutes.
/// Resets TrnStat=1 rows that were dispatched but never ACKed within 5 minutes
/// back to TrnStat=0 (or TrnStat=9 if retry count exceeded).
/// This is the safety net for crashed clients.
/// </summary>
public class StallRecoveryService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<StallRecoveryService> _logger;

    public StallRecoveryService(IServiceScopeFactory scopeFactory, ILogger<StallRecoveryService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);

                using var scope = _scopeFactory.CreateScope();
                var repo = scope.ServiceProvider.GetRequiredService<ICommTrnRepository>();
                await repo.ResetStalledDispatchesAsync(timeoutMinutes: 5);

                _logger.LogDebug("StallRecovery: checked for stalled dispatches.");
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "StallRecovery: unexpected error.");
            }
        }
    }
}
