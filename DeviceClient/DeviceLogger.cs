using System.Threading.Channels;
using System.Text;

/// <summary>
/// Per-device logger. Each DeviceSimulator creates its own instance so that
/// log files are never shared between devices and interleaving is impossible.
///
/// Log folder layout:
///   logs/{SafeMAC}/info.log
///   logs/{SafeMAC}/debug.log
///   logs/{SafeMAC}/error.log
///   logs/{SafeMAC}/warn.log
///
/// where {SafeMAC} is the MAC address with colons replaced by dashes,
/// e.g.  00-1B-09-00-00-01
/// </summary>
public class DeviceLogger
{
    // ── Channels ───────────────────────────────────────────────────────────────
    private readonly Channel<string> _infoCh;
    private readonly Channel<string> _debugCh;
    private readonly Channel<string> _errorCh;
    private readonly Channel<string> _warnCh;

    // ── Feature flags ──────────────────────────────────────────────────────────
    private readonly bool _enableInfo;
    private readonly bool _enableDebug;
    private readonly bool _enableError;
    private readonly bool _enableWarn;

    // ── Drop counters (thread-safe) ────────────────────────────────────────────
    private long _droppedInfo;
    private long _droppedDebug;
    private long _droppedError;
    private long _droppedWarn;

    // ── Tuning ─────────────────────────────────────────────────────────────────
    private const int CHANNEL_CAPACITY = 200_000;
    private const int BATCH_SIZE       = 500;
    private static readonly TimeSpan FLUSH_INTERVAL = TimeSpan.FromMilliseconds(150);

    // ── Identity (used in drop-stat messages) ──────────────────────────────────
    private readonly string _label;   // e.g. "[00:1B:09:00:00:01]"

    // ── Constructor ────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates a DeviceLogger whose files land in
    ///   logs/{macAddr with ':' → '-'}/
    /// </summary>
    public DeviceLogger(DeviceInfo device, LoggingSection cfg)
    {
        _label = $"[{device.MACAddr}]";

        _enableInfo  = cfg.EnableInfo;
        _enableDebug = cfg.EnableDebug;
        _enableError = cfg.EnableError;
        _enableWarn  = cfg.EnableWarn;

        // Build the per-device log directory
        string safeFolder = device.MACAddr.Replace(':', '-');
        string baseDir    = cfg.LogBaseDir ?? "logs";
        string deviceDir  = Path.Combine(baseDir, safeFolder);
        Directory.CreateDirectory(deviceDir);

        // Derive file paths inside that folder
        string infoFile  = Path.Combine(deviceDir, "info.log");
        string debugFile = Path.Combine(deviceDir, "debug.log");
        string errorFile = Path.Combine(deviceDir, "error.log");
        string warnFile  = Path.Combine(deviceDir, "warn.log");

        var opt = new BoundedChannelOptions(CHANNEL_CAPACITY)
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

        Task.Run(DropStatLoop);
    }

    // ── Public API ─────────────────────────────────────────────────────────────

    public void Info(string msg)
    {
        if (!_enableInfo) return;
        Write(_infoCh, "INFO ", msg, () => Interlocked.Increment(ref _droppedInfo));
    }

    public void Debug(string msg)
    {
        if (!_enableDebug) return;
        Write(_debugCh, "DEBUG", msg, () => Interlocked.Increment(ref _droppedDebug));
    }

    public void Error(string msg)
    {
        if (!_enableError) return;
        Write(_errorCh, "ERROR", msg, () => Interlocked.Increment(ref _droppedError));

        // Mirror errors to warn file so warn.log is a superset
        if (_enableWarn)
            Write(_warnCh, "ERROR", $"[mirrored] {msg}", () => Interlocked.Increment(ref _droppedWarn));
    }

    public void Warn(string msg)
    {
        if (!_enableWarn) return;
        Write(_warnCh, "WARN ", msg, () => Interlocked.Increment(ref _droppedWarn));
    }

    /// <summary>Logs an unexpected/mismatched value with context.</summary>
    public void Mismatch(string context, string field, object? expected, object? actual, bool isError = false)
    {
        var msg = $"{context} | MISMATCH | Field={field} | Expected={expected ?? "null"} | Actual={actual ?? "null"}";
        Warn(msg);
        if (isError) Error(msg);
    }

    /// <summary>Logs a missing/null field that should have been present.</summary>
    public void Missing(string context, string field, string? hint = null)
    {
        var msg = $"{context} | MISSING | Field={field}" + (hint != null ? $" | Hint={hint}" : "");
        Warn(msg);
    }

    /// <summary>Logs an unexpected server response structure.</summary>
    public void UnexpectedResponse(string context, int statusCode, string body, string? reason = null)
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
                Console.Error.WriteLine($"[DeviceLogger] Cannot open '{file}' (attempt {attempt}): {ex.Message}");
                await Task.Delay(500);
            }
        }

        if (fs == null)
        {
            Console.Error.WriteLine($"[DeviceLogger] FATAL: giving up on '{file}'. Logs will be lost.");
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
                    Console.Error.WriteLine($"[DeviceLogger] Write failed for '{file}': {ex.Message}");
                }

                buffer.Clear();
            }
        }
    }

    private async Task DropStatLoop()
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
                var msg = $"{_label} [DeviceLogger] DROPPED in last 60s | " +
                          $"Info={di} Debug={dd} Error={de} Warn={dw}";
                Console.Error.WriteLine(msg);
                Write(_warnCh, "WARN ", msg, () => Interlocked.Increment(ref _droppedWarn));
            }
        }
    }

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + $"…[+{s.Length - max} chars]";
}