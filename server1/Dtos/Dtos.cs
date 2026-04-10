namespace MatGenServer.DTOs;

// ── Auth ──────────────────────────────────────────────────────────────────────

public class LoginRequestDto
{
    public string DeviceID { get; set; } = string.Empty;
    public string MACAddr { get; set; } = string.Empty;
    public string IPAddr { get; set; } = string.Empty;
}

public class LoginResponseDto
{
    public bool Success { get; set; }
    public string? Message { get; set; }
    public string? AccessToken { get; set; }
    public string? DeviceName { get; set; }
    public string? DeviceType { get; set; }
}

public class RefreshTokenResponseDto
{
    public bool Success { get; set; }
    public string? AccessToken { get; set; }
    public string? Message { get; set; }
}

// ── Polling ───────────────────────────────────────────────────────────────────

public class PollRequestDto
{
    /// <summary>Device sends back the last batch token it processed (null on first poll).</summary>
    public string? LastBatchToken { get; set; }
}

public class TrnItemDto
{
    public decimal TrnID { get; set; }
    public string? MsgStr { get; set; }
    public int RetryCnt { get; set; }
}

public class PollResponseDto
{
    public bool HasData { get; set; }

    /// <summary>
    /// Unique opaque token for THIS batch.
    /// Client must echo this token in AckRequestDto.BatchToken.
    /// </summary>
    public string? BatchToken { get; set; }
    public List<TrnItemDto> Items { get; set; } = new();

    /// <summary>
    /// If true, a previous batch is still pending ACK.
    /// Client should NOT process new items – just ACK the pending batch first.
    /// </summary>
    public bool PendingAckRequired { get; set; }
    public string? PendingBatchToken { get; set; }
}

// ── Acknowledge ───────────────────────────────────────────────────────────────

public class AckRequestDto
{
    /// <summary>Batch token received in PollResponseDto.BatchToken</summary>
    public string BatchToken { get; set; } = string.Empty;
    public List<decimal> AckedTrnIDs { get; set; } = new();
}

public class AckResponseDto
{
    public bool Success { get; set; }
    public int UpdatedCount { get; set; }
    public string? Message { get; set; }
}