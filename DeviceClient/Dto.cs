public class PollResponse
{
    public bool HasData { get; set; }
    public bool NeedAckFirst { get; set; }
    public int TotalPending { get; set; }
    public string? TypeMID { get; set; }
    public List<TrnRow> Rows { get; set; } = new();
}

public class TrnRow
{
    public decimal TrnID { get; set; }
    public string? MsgStr { get; set; }
    public int RetryCnt { get; set; }
    public string? TypeMID { get; set; }
}

public class DeviceConfig
{
    public required DeviceSection Device { get; set; }
    public required TimingSection Timing { get; set; }
    public required EventSection Event { get; set; }
    public required ServerSection Server { get; set; }
    public required LoggingSection Logging { get; set; }
}

public class DeviceSection
{
    public int DeviceId { get; set; }
    public required string MacAddress { get; set; }
    public required string IpAddress { get; set; }
}

public class TimingSection
{
    public int PollIntervalSeconds { get; set; }
    public int EventIntervalSeconds { get; set; }
    public int BulkEverySeconds { get; set; }
}

public class EventSection
{
    public int EventCount { get; set; }
    public int BulkCount { get; set; }
    public bool EnableBulkMode { get; set; }
}

public class ServerSection
{
    public required string BaseUrl { get; set; }
}

public class LoggingSection
{
    public required string InfoFile { get; set; }
    public required string DebugFile { get; set; }
    public required string ErrorFile { get; set; }

    public bool EnableInfo { get; set; }
    public bool EnableDebug { get; set; }
    public bool EnableError { get; set; }
}