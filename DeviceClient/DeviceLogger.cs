using System.Collections.Concurrent;

public static class DeviceLogger
{
    private static readonly BlockingCollection<(LogLevel, string)> _queue = new();

    private static string _infoFile = "";
    private static string _debugFile = "";
    private static string _errorFile = "";

    private static bool _enableInfo;
    private static bool _enableDebug;
    private static bool _enableError;

    private static bool _started = false;

    public static void Configure(
        string infoFile,
        string debugFile,
        string errorFile,
        bool enableInfo,
        bool enableDebug,
        bool enableError)
    {
        _infoFile = infoFile;
        _debugFile = debugFile;
        _errorFile = errorFile;

        _enableInfo = enableInfo;
        _enableDebug = enableDebug;
        _enableError = enableError;

        if (!_started)
        {
            _started = true;
            Task.Run(ProcessQueue);
        }
    }

    public static void Info(string msg)  { if (_enableInfo) Enqueue(LogLevel.Info, msg); }
    public static void Debug(string msg) { if (_enableDebug) Enqueue(LogLevel.Debug, msg); }
    public static void Error(string msg) { if (_enableError) Enqueue(LogLevel.Error, msg); }

    private static void Enqueue(LogLevel level, string msg)
    {
        var line = $"{DateTime.Now:MM-dd-yyyy HH:mm:ss.fff} | {level} | {msg}";
        _queue.Add((level, line));
    }

    private static async Task ProcessQueue()
    {
        foreach (var (level, line) in _queue.GetConsumingEnumerable())
        {
            try
            {
                var file = level switch
                {
                    LogLevel.Info => _infoFile,
                    LogLevel.Debug => _debugFile,
                    LogLevel.Error => _errorFile,
                    _ => null
                };

                if (string.IsNullOrWhiteSpace(file))
                    continue;

                // ALWAYS ensure directory here (not in Configure)
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);

                await File.AppendAllTextAsync(file, line + Environment.NewLine);
            }
            catch
            {
                // swallow — logger must NEVER crash app
            }
        }
    }
}