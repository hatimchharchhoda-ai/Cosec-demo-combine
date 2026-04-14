public static class DeviceLogger
{
    private static readonly string path = "device-log.txt";

    public static void Log(string msg)
    {
        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} | {msg}";
        File.AppendAllText(path, line + Environment.NewLine);
        Console.WriteLine(line);
    }
}