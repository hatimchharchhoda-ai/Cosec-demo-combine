namespace MatPoll.DTOs;

// ── Login ─────────────────────────────────────────────────────────────────────
// Client sends: UserID + MACAddr + IPAddr
// Server finds matching device by MAC + IP, checks user is active

public class LoginRequest
{
    public string UserID  { get; set; } = string.Empty;   // e.g. "USR001"
    public string MACAddr { get; set; } = string.Empty;   // e.g. "AA:BB:CC:DD:EE:01"
    public string IPAddr  { get; set; } = string.Empty;   // e.g. "192.168.1.101"
}

public class LoginResponse
{
    public bool    Success    { get; set; }
    public string? Message    { get; set; }
    public string? DeviceName { get; set; }
    // Token is also set in HttpOnly cookie — see AuthController
    // We return it in body too so non-browser clients can use it
    public string? Token      { get; set; }
}

// ── Refresh Token ─────────────────────────────────────────────────────────────
// Client sends empty POST — server reads token from cookie, issues new one

public class RefreshResponse
{
    public bool    Success { get; set; }
    public string? Message { get; set; }
    public string? Token   { get; set; }
}

// ── Poll ──────────────────────────────────────────────────────────────────────
// Client sends GET every 8 seconds
// Server returns rows OR tells client to ACK pending batch first

public class PollResponse
{
    public bool           HasData          { get; set; }
    public string?        BatchToken       { get; set; }

    // true = you have an un-ACKed batch — send ACK first, then poll again
    public bool           NeedAckFirst     { get; set; }

    public List<TrnRow>   Rows             { get; set; } = new();
    public int            TotalPending     { get; set; } // how many TrnStat=0 remain in DB
}

public class TrnRow
{
    public decimal TrnID    { get; set; }
    public string? MsgStr   { get; set; }
    public decimal RetryCnt { get; set; }
}

// ── ACK ───────────────────────────────────────────────────────────────────────
// Client sends after processing rows

public class AckRequest
{
    public string         BatchToken { get; set; } = string.Empty;
    public List<decimal>  TrnIDs     { get; set; } = new();
}

public class AckResponse
{
    public bool   Success      { get; set; }
    public string Message      { get; set; } = string.Empty;
    public int    UpdatedCount { get; set; }
}
