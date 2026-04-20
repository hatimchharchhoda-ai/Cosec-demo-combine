using System.Threading.Channels;
using System.Text;

public static class DeviceLogger
{
    private static Channel<string> _infoCh = null!;
    private static Channel<string> _debugCh = null!;
    private static Channel<string> _errorCh = null!;
    private static Channel<string> _walCh = null!;

    private static string _infoFile = "";
    private static string _debugFile = "";
    private static string _errorFile = "";
    private static string _walFile = "";

    private static bool _enableInfo;
    private static bool _enableDebug;
    private static bool _enableError;

    private const int BATCH_SIZE = 20;
    private static readonly TimeSpan FLUSH_INTERVAL = TimeSpan.FromMilliseconds(150);

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
        _walFile = Path.Combine(Path.GetDirectoryName(infoFile)!, "logger.wal");

        _enableInfo = enableInfo;
        _enableDebug = enableDebug;
        _enableError = enableError;

        Directory.CreateDirectory(Path.GetDirectoryName(infoFile)!);

        var opt = new BoundedChannelOptions(200_000)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
            SingleWriter = false
        };

        _infoCh = Channel.CreateBounded<string>(opt);
        _debugCh = Channel.CreateBounded<string>(opt);
        _errorCh = Channel.CreateBounded<string>(opt);
        _walCh = Channel.CreateUnbounded<string>();

        // Start dedicated consumers
        Task.Run(() => WriterLoop(_infoCh, _infoFile));
        Task.Run(() => WriterLoop(_debugCh, _debugFile));
        Task.Run(() => WriterLoop(_errorCh, _errorFile));

        // WAL writer (never blocks callers)
        Task.Run(WalLoop);

        // Replay WAL if exists (crash recovery)
        ReplayWal();
    }

    public static void Info(string msg)
    {
        if (!_enableInfo) return;
        Write(_infoCh, "INFO", msg);
    }

    public static void Debug(string msg)
    {
        if (!_enableDebug) return;
        Write(_debugCh, "DEBUG", msg);
    }

    public static void Error(string msg)
    {
        if (!_enableError) return;
        Write(_errorCh, "ERROR", msg);
    }

    // private static void Enqueue(LogLevel level, string msg)
    // {
    //     var line = $"{DateTime.Now:dd-MM-yyyy HH:mm:ss.fff} | {level} | {msg}";
    //     var item = new LogItem(level, line);

    //     if (!_channel.Writer.TryWrite(item))
    //     {
    //         // Queue full → spill to WAL (disk)
    //         File.AppendAllText(_walFile, line + Environment.NewLine);
    //     }
    // }

    private static void Write(Channel<string> ch, string level, string msg)
    {
        var line = $"{DateTime.Now:dd-MM-yyyy HH:mm:ss.fff} | {level} | {msg}";

        if (!ch.Writer.TryWrite(line))
        {
            // spill to WAL channel (async, non-blocking)
            _walCh.Writer.TryWrite(line);
        }
    }

    private static async Task WriterLoop(Channel<string> ch, string file)
    {
        using var fs = new FileStream(
            file,
            FileMode.Append,
            FileAccess.Write,
            FileShare.Read,
            128 * 1024,
            useAsync: true);

        using var writer = new StreamWriter(fs, Encoding.UTF8);

        var buffer = new List<string>(BATCH_SIZE);
        var timer = new PeriodicTimer(FLUSH_INTERVAL);

        while (await timer.WaitForNextTickAsync())
        {
            while (buffer.Count < BATCH_SIZE && ch.Reader.TryRead(out var line))
                buffer.Add(line);

            if (buffer.Count == 0)
                continue;

            foreach (var line in buffer)
                await writer.WriteLineAsync(line);

            await writer.FlushAsync();
            buffer.Clear();
        }
    }

    // Dedicated WAL disk writer (never from HTTP thread)
    private static async Task WalLoop()
    {
        using var fs = new FileStream(
            _walFile,
            FileMode.Append,
            FileAccess.Write,
            FileShare.Read,
            64 * 1024,
            useAsync: true);

        using var writer = new StreamWriter(fs, Encoding.UTF8);

        await foreach (var line in _walCh.Reader.ReadAllAsync())
        {
            await writer.WriteLineAsync(line);
            await writer.FlushAsync();
        }
    }

    // Crash safety — replay WAL
    private static void ReplayWal()
    {
        if (!File.Exists(_walFile)) return;

        var lines = File.ReadAllLines(_walFile);
        File.Delete(_walFile);

        foreach (var line in lines)
        {
            if (line.Contains("| INFO |"))
                _infoCh.Writer.TryWrite(line);
            else if (line.Contains("| DEBUG |"))
                _debugCh.Writer.TryWrite(line);
            else if (line.Contains("| ERROR |"))
                _errorCh.Writer.TryWrite(line);
        }
    }

    private record LogItem(LogLevel Level, string Line);
}