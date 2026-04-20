public static class ConnectionSupervisor
{
    public static void Start(ApiClient api, DeviceConfig cfg)
    {
        _ = Task.Run(async () =>
        {
            while (true)
            {
                if (!DeviceState.IsConnected)
                {
                    DeviceLogger.Error("RECONNECT LOOP | Attempting login...");

                    try
                    {
                        await api.Login(
                            cfg.Device.DeviceType,
                            cfg.Device.MacAddress,
                            cfg.Device.IpAddress);

                        if (DeviceState.IsConnected)
                        {
                            await api.Restore();
                        }
                    }
                    catch (Exception ex)
                    {
                        DeviceLogger.Error($"RECONNECT FAILED | {ex.Message}");
                        
                        DeviceState.SetDisconnected("Login exception");
                    }
                }

                await Task.Delay(5000);
            }
        });
    }
}