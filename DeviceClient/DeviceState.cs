public static class DeviceState
{
    private static volatile bool _connected = false;

    public static bool IsConnected => _connected;

    public static void SetConnected()
    {
        _connected = true;
        DeviceLogger.Info("DEVICE STATE → CONNECTED");
    }

    public static void SetDisconnected(string reason)
    {
        if (_connected)
            DeviceLogger.Error($"DEVICE STATE → DISCONNECTED | Reason: {reason}");

        _connected = false;
    }
}