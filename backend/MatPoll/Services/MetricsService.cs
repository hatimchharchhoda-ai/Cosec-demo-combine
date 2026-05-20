using MatPoll.Models;
using System.Diagnostics;

namespace MatPoll.Services;

public class MetricsSnapshot
{
    public int PollCount { get; set; }
    public double AvgPollMs { get; set; }
    public long PeakPollMs { get; set; }
    public string PollStatus { get; set; } = "good";

    public int AckCount { get; set; }
    public double AvgAckMs { get; set; }
    public long PeakAckMs { get; set; }
    public string AckStatus { get; set; } = "good";

    public int EventCount { get; set; }
    public double AvgEventMs { get; set; }
    public long PeakEventMs { get; set; }

    public double ReqPerSec { get; set; }
    public string ReqStatus { get; set; } = "good";

    public int ErrorCount { get; set; }

    public double CpuPercent { get; set; }
    public string CpuStatus { get; set; } = "good";

    public long MemoryMb { get; set; }
    public string MemStatus { get; set; } = "good";

    public int DbConnections { get; set; }
    public string DbConnStatus { get; set; } = "good";

    public int ActiveDevices { get; set; }
    public int CommTrnPending { get; set; }
    public string CommTrnStatus { get; set; } = "good";
    public int CommTrnTotal { get; set; }
}

public class MetricsService : BackgroundService
{
    private readonly ActivityLogger _actLog;
    public MetricsSnapshot? LastSnapshot { get; private set; }

    // Counters — call these from your controllers
    private long _pollCount, _pollTotalMs, _pollPeakMs;
    private long _ackCount, _ackTotalMs, _ackPeakMs;
    private long _eventCount, _eventTotalMs, _eventPeakMs;
    private long _errorCount;

    private readonly object _lock = new();

    public MetricsService(ActivityLogger actLog)
    {
        _actLog = actLog;
    }

    // ── Call these from your Poll/Ack/Event controllers ──
    public void RecordPoll(long ms)
    {
        lock (_lock)
        {
            _pollCount++;
            _pollTotalMs += ms;
            if (ms > _pollPeakMs) _pollPeakMs = ms;
        }
    }

    public void RecordAck(long ms)
    {
        lock (_lock)
        {
            _ackCount++;
            _ackTotalMs += ms;
            if (ms > _ackPeakMs) _ackPeakMs = ms;
        }
    }

    public void RecordEvent(long ms)
    {
        lock (_lock)
        {
            _eventCount++;
            _eventTotalMs += ms;
            if (ms > _eventPeakMs) _eventPeakMs = ms;
        }
    }

    public void RecordError()
    {
        Interlocked.Increment(ref _errorCount);
    }

    // ── Background loop — snapshots every 60s ──
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(60), ct);
            TakeSnapshot();
            _actLog.LogMetricsSummary();
        }
    }

    private void TakeSnapshot()
    {
        long pollCount, pollTotal, pollPeak;
        long ackCount, ackTotal, ackPeak;
        long eventCount, eventTotal, eventPeak;
        long errorCount;

        // Grab and reset counters atomically
        lock (_lock)
        {
            pollCount = _pollCount;   pollTotal = _pollTotalMs;  pollPeak = _pollPeakMs;
            ackCount  = _ackCount;    ackTotal  = _ackTotalMs;   ackPeak  = _ackPeakMs;
            eventCount = _eventCount; eventTotal = _eventTotalMs; eventPeak = _eventPeakMs;
            errorCount = _errorCount;

            _pollCount = _pollTotalMs = _pollPeakMs = 0;
            _ackCount  = _ackTotalMs  = _ackPeakMs  = 0;
            _eventCount = _eventTotalMs = _eventPeakMs = 0;
            _errorCount = 0;
        }

        var totalReqs = pollCount + ackCount + eventCount;
        var memMb = Process.GetCurrentProcess().WorkingSet64 / 1024 / 1024;

        LastSnapshot = new MetricsSnapshot
        {
            // Requests
            PollCount  = (int)pollCount,
            AvgPollMs  = pollCount > 0 ? (double)pollTotal / pollCount : 0,
            PeakPollMs = pollPeak,
            PollStatus = pollPeak >= 200 ? "crit" : pollPeak >= 50 ? "warn" : "good",

            AckCount   = (int)ackCount,
            AvgAckMs   = ackCount > 0 ? (double)ackTotal / ackCount : 0,
            PeakAckMs  = ackPeak,
            AckStatus  = ackPeak >= 500 ? "crit" : ackPeak >= 100 ? "warn" : "good",

            EventCount  = (int)eventCount,
            AvgEventMs  = eventCount > 0 ? (double)eventTotal / eventCount : 0,
            PeakEventMs = eventPeak,

            ReqPerSec  = totalReqs / 60.0,
            ReqStatus  = (totalReqs / 60.0) >= 200 ? "crit" : (totalReqs / 60.0) >= 50 ? "warn" : "good",

            ErrorCount = (int)errorCount,

            // System
            CpuPercent = GetCpuUsage(),
            CpuStatus  = GetCpuUsage() >= 50 ? "crit" : GetCpuUsage() >= 20 ? "warn" : "good",

            MemoryMb   = memMb,
            MemStatus  = memMb >= 500 ? "crit" : memMb >= 200 ? "warn" : "good",

            DbConnections = 0, // wire up if needed
            DbConnStatus  = "good",

            // DB — wire these up from your DbContext if needed
            ActiveDevices  = 0,
            CommTrnPending = 0,
            CommTrnStatus  = "good",
            CommTrnTotal   = 0,
        };
    }

    private static double GetCpuUsage()
    {
        // Simple approximation — replace with PerformanceCounter if needed
        var start = Process.GetCurrentProcess().TotalProcessorTime;
        Thread.Sleep(100);
        var end = Process.GetCurrentProcess().TotalProcessorTime;
        return (end - start).TotalMilliseconds / (Environment.ProcessorCount * 100.0);
    }
}
