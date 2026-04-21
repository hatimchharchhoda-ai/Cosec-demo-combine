public static class ConnectionSupervisor
{
    public static void Start(ApiClient api, DeviceConfig cfg)
    {
        _ = Task.Run(async () =>
        {
            bool attempting = false;

            while (true)
            {
                if (!DeviceState.IsConnected && !attempting)
                {
                    attempting = true;

                    try
                    {
                        DeviceLogger.Error("RECONNECT | Login attempt");

                        await api.Login(
                            cfg.Device.DeviceType,
                            cfg.Device.MacAddress,
                            cfg.Device.IpAddress);

                        if (DeviceState.IsConnected)
                        {
                            DeviceLogger.Info("RECONNECT | Restore after login");
                            await api.Restore();
                        }
                    }
                    catch (Exception ex)
                    {
                        DeviceLogger.Error($"RECONNECT FAILED | {ex}");
                    }
                    finally
                    {
                        attempting = false;
                    }
                }

                await Task.Delay(5000);
            }
        });
    }
}