using MatPoll.Repositories;
using MatPoll.Services;

namespace MatPoll.Services;

public class DeviceStatusService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ActivityLogger       _actLog;
    private readonly IConfiguration       _config;

    public DeviceStatusService(
        IServiceScopeFactory scopeFactory,
        ActivityLogger actLog,
        IConfiguration config)
    {
        _scopeFactory = scopeFactory;
        _actLog       = actLog;
        _config       = config;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            // run every N minutes from config
            var intervalMinutes = _config.GetValue<int>(
                "PollingSettings:DeviceOfflineCheckMinutes", 2);

            // await _repo.UpdateLastSeenAsync(deviceId);
            await Task.Delay(TimeSpan.FromMinutes(intervalMinutes), ct);

            try
            {
                await CheckDevicesAsync();
            }
            catch (Exception ex)
            {
                _actLog.LogDbFailure("DEVICE-STATUS-CHECK", ex);
            }
        }
    }

    private async Task CheckDevicesAsync()
    {
        var timeoutMinutes = _config.GetValue<int>(
            "PollingSettings:DeviceOfflineTimeoutMinutes", 2);

        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<AppRepository>();

        // find devices that have gone silent
        var staleDevices = await repo.GetStaleDevicesAsync(timeoutMinutes);

        if (staleDevices.Count == 0) return;

        var ids = staleDevices.Select(d => d.DeviceID).ToList();

        // mark them offline
        await repo.MarkDevicesOfflineAsync(ids);

        // log each one
        foreach (var device in staleDevices)
        {
            _actLog.LogDeviceOffline(
                device.DeviceID,
                device.DeviceName ?? "?",
                device.DeviceType ?? 0,
                device.LastSeenAt);
        }
    }
}
