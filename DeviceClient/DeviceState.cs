public static class DeviceState
{
    private static volatile bool _connected = false;

    public static bool IsConnected => _connected;

    public static void SetConnected(DeviceLogger logger)
    {
        _connected = true;
        logger.Info("DEVICE STATE → CONNECTED");
    }

    public static void SetDisconnected(string reason, DeviceLogger logger)
    {
        if (_connected)
            logger.Error($"DEVICE STATE → DISCONNECTED | Reason: {reason}");

        _connected = false;
    }
}