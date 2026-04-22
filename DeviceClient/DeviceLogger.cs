using System.Threading.Channels;
using System.Text;

public static class DeviceLogger
{
    private static Channel<string> _infoCh  = null!;
    private static Channel<string> _debugCh = null!;
    private static Channel<string> _errorCh = null!;
    private static Channel<string> _warnCh  = null!;

    private static bool _enableInfo;
    private static bool _enableDebug;
    private static bool _enableError;
    private static bool _enableWarn;

    // Drop-tracking counters (thread-safe)
    private static long _droppedInfo;
    private static long _droppedDebug;
    private static long _droppedError;
    private static long _droppedWarn;

    private const int BATCH_SIZE = 20;
    private static readonly TimeSpan FLUSH_INTERVAL = TimeSpan.FromMilliseconds(150);

    public static void Configure(
        string infoFile,
        string debugFile,
        string errorFile,
        string warnFile,
        bool enableInfo,
        bool enableDebug,
        bool enableError,
        bool enableWarn)
    {
        _enableInfo  = enableInfo;
        _enableDebug = enableDebug;
        _enableError = enableError;
        _enableWarn  = enableWarn;

        EnsureDir(infoFile);
        EnsureDir(debugFile);
        EnsureDir(errorFile);
        EnsureDir(warnFile);
        
        var opt = new BoundedChannelOptions(200_000)
        {
            FullMode     = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
            SingleWriter = false
        };

        _infoCh  = Channel.CreateBounded<string>(opt);
        _debugCh = Channel.CreateBounded<string>(opt);
        _errorCh = Channel.CreateBounded<string>(opt);
        _warnCh  = Channel.CreateBounded<string>(opt);

        Task.Run(() => WriterLoop(_infoCh,  infoFile));
        Task.Run(() => WriterLoop(_debugCh, debugFile));
        Task.Run(() => WriterLoop(_errorCh, errorFile));
        Task.Run(() => WriterLoop(_warnCh,  warnFile));

        // Periodically log any drop stats to warn channel
        Task.Run(DropStatLoop);
    }

    // ── Public API ─────────────────────────────────────────────────────────────

    public static void Info(string msg)
    {
        if (!_enableInfo) return;
        Write(_infoCh, "INFO ", msg, () => Interlocked.Increment(ref _droppedInfo));
    }

    public static void Debug(string msg)
    {
        if (!_enableDebug) return;
        Write(_debugCh, "DEBUG", msg, () => Interlocked.Increment(ref _droppedDebug));
    }

    public static void Error(string msg)
    {
        if (!_enableError) return;
        Write(_errorCh, "ERROR", msg, () => Interlocked.Increment(ref _droppedError));

        if (_enableWarn)
            Write(_warnCh, "ERROR", $"[mirrored] {msg}", () => Interlocked.Increment(ref _droppedWarn));
    }

    public static void Warn(string msg)
    {
        if (!_enableWarn) return;
        Write(_warnCh, "WARN ", msg, () => Interlocked.Increment(ref _droppedWarn));
    }

    // Logs an unexpected/mismatched value with context. Emits WARN + optional ERROR.
    public static void Mismatch(string context, string field, object? expected, object? actual, bool isError = false)
    {
        var msg = $"{context} | MISMATCH | Field={field} | Expected={expected ?? "null"} | Actual={actual ?? "null"}";
        Warn(msg);
        if (isError) Error(msg);
    }

    // Logs a missing/null field that should have been present.
    public static void Missing(string context, string field, string? hint = null)
    {
        var msg = $"{context} | MISSING | Field={field}" + (hint != null ? $" | Hint={hint}" : "");
        Warn(msg);
    }

    // Logs an unexpected server response structure.
    public static void UnexpectedResponse(string context, int statusCode, string body, string? reason = null)
    {
        var msg = $"{context} | UNEXPECTED-RESPONSE | Status={statusCode}" +
                  (reason != null ? $" | Reason={reason}" : "") +
                  $" | Body={Truncate(body, 500)}";
        Error(msg);
    }

    // ── Internals ──────────────────────────────────────────────────────────────

    private static void Write(Channel<string> ch, string level, string msg, Action onDrop)
    {
        var line = $"{DateTime.Now:dd-MM-yyyy HH:mm:ss.fff} | {level} | {msg}";
        if (!ch.Writer.TryWrite(line))
            onDrop();
    }

    private static async Task WriterLoop(Channel<string> ch, string file)
    {
        // Retry opening the file up to 5 times (handles locked/missing dir race)
        FileStream? fs = null;
        for (int attempt = 1; attempt <= 5; attempt++)
        {
            try
            {
                fs = new FileStream(
                    file,
                    FileMode.Append,
                    FileAccess.Write,
                    FileShare.ReadWrite,
                    128 * 1024,
                    useAsync: true);
                break;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[DeviceLogger] Cannot open log file '{file}' (attempt {attempt}): {ex.Message}");
                await Task.Delay(500);
            }
        }

        if (fs == null)
        {
            Console.Error.WriteLine($"[DeviceLogger] FATAL: giving up on log file '{file}'. Logs will be lost.");
            return;
        }

        using (fs)
        using (var writer = new StreamWriter(fs, Encoding.UTF8))
        {
            var buffer = new List<string>(BATCH_SIZE);
            var timer  = new PeriodicTimer(FLUSH_INTERVAL);

            while (await timer.WaitForNextTickAsync())
            {
                while (buffer.Count < BATCH_SIZE && ch.Reader.TryRead(out var line))
                    buffer.Add(line);

                if (buffer.Count == 0) continue;

                try
                {
                    foreach (var line in buffer)
                        await writer.WriteLineAsync(line);

                    await writer.FlushAsync();
                }
                catch (Exception ex)
                {
                    // Last resort: emit to stderr so the process isn't totally silent
                    Console.Error.WriteLine($"[DeviceLogger] Write failed for '{file}': {ex.Message}");
                }

                buffer.Clear();
            }
        }
    }

    private static async Task DropStatLoop()
    {
        while (true)
        {
            await Task.Delay(TimeSpan.FromSeconds(60));

            long di = Interlocked.Exchange(ref _droppedInfo,  0);
            long dd = Interlocked.Exchange(ref _droppedDebug, 0);
            long de = Interlocked.Exchange(ref _droppedError, 0);
            long dw = Interlocked.Exchange(ref _droppedWarn,  0);

            if (di + dd + de + dw > 0)
            {
                var msg = $"[DeviceLogger] DROPPED MESSAGES in last 60s | " +
                          $"Info={di} Debug={dd} Error={de} Warn={dw}";
                // Write directly to stderr as well so it's not also dropped
                Console.Error.WriteLine(msg);
                Write(_warnCh, "WARN ", msg, () => Interlocked.Increment(ref _droppedWarn));
            }
        }
    }

    private static void EnsureDir(string filePath)
    {
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + $"…[+{s.Length - max} chars]";
}