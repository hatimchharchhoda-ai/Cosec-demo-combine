public static class DeviceLogger
{
    private static readonly string logPath =
        Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "device-log.txt");

    public static void Log(string message)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} | {message}";
        File.AppendAllText(logPath, line + Environment.NewLine);
        Console.WriteLine(line);
    }
}