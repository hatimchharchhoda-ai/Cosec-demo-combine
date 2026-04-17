namespace MatPoll.DTOs;

// ── Login ─────────────────────────────────────────────────────────────────────
public class LoginRequest
{
    public decimal DeviceID { get; set; }
    public string  MACAddr  { get; set; } = string.Empty;
    public string  IPAddr   { get; set; } = string.Empty;
    public DateTime?  T1    { get; set; }
}

public class LoginResponse
{
    public bool    Success    { get; set; }
    public string? Message    { get; set; }
    public string? DeviceName { get; set; }
    public string? Token      { get; set; }
    // TypeMID sent back so client knows its own identifier
    public string? TypeMID    { get; set; }
    public DateTime? ServerSentAt { get; set; }
}

// ── Refresh ───────────────────────────────────────────────────────────────────
public class RefreshResponse
{
    public bool    Success { get; set; }
    public string? Message { get; set; }
    public string? Token   { get; set; }
    public string? TypeMID { get; set; }
    public DateTime? ServerSentAt { get; set; }
}

// ── Poll ──────────────────────────────────────────────────────────────────────
public class PollResponse
{
    public bool         HasData      { get; set; }
    // NeedAckFirst = true means: TrnStat=1 rows exist for this TypeMID
    // Client must ACK those before getting new data
    public bool         NeedAckFirst { get; set; }
    public List<TrnRow> Rows         { get; set; } = new();
    public int          TotalPending { get; set; }
    public string?      TypeMID      { get; set; }
    public DateTime?    ServerSentAt { get; set; }
}

public class TrnRow
{
    public decimal TrnID    { get; set; }
    public string? MsgStr   { get; set; }
    public decimal RetryCnt { get; set; }
    public string? TypeMID  { get; set; }
}

// ── ACK ───────────────────────────────────────────────────────────────────────
public class AckRequest
{
    public List<decimal> TrnIDs      { get; set; } = new();
    public DateTime?     T1          { get; set; }  // client sent THIS request
    public DateTime?     T4Prev      { get; set; }  // client received PREVIOUS ack response
}
public class AckResponse
{
    public bool      Success      { get; set; }
    public string    Message      { get; set; } = string.Empty;
    public int       UpdatedCount { get; set; }
    public DateTime? ServerSentAt { get; set; }
}

// ── Restore ───────────────────────────────────────────────────────────────────
// Client calls this to reset all TrnStat=1 rows for its TypeMID back to 0
public class RestoreResponse
{
    public bool   Success      { get; set; }
    public string Message      { get; set; } = string.Empty;
    public int    RestoredCount { get; set; }
    public string? TypeMID     { get; set; }
    public DateTime? ServerSentAt { get; set; }
}

// ── ACK ───────────────────────────────────────────────────────────────────────



public class DeviceEventDto
{
    public string TypeMID { get; set; }
    public string Message { get; set; }
}