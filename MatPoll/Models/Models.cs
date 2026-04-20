using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MatPoll.Models;

[Table("Mat_DeviceMst")]
public class MatDeviceMst
{
    [Key]
    [Column(TypeName = "numeric(5,0)")]
    public decimal DeviceID { get; set; }

    public string? DeviceName { get; set; }

    [StringLength(50)]
    public string? MACAddr { get; set; }

    [StringLength(50)]
    public string? IPAddr { get; set; }

    [Column(TypeName = "numeric(1,0)")]
    public decimal? IsActive { get; set; }

    [Column(TypeName = "numeric(2,0)")]
    public decimal? DeviceType { get; set; }
}

[Table("Mat_UserMst")]
public class MatUserMst
{
    [Key]
    [StringLength(10)]
    public string UserID { get; set; } = string.Empty;

    public string?  UserName      { get; set; }

    [Column(TypeName = "numeric(1,0)")]
    public decimal? IsActive      { get; set; }

    [StringLength(16)]
    public string?  UserShortName { get; set; }

    [Column(TypeName = "numeric(20,0)")]
    public decimal? UserIDN       { get; set; }
}

[Table("Mat_CommTrn")]
public class MatCommTrn
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column(TypeName = "numeric(18,0)")]
    public decimal TrnID { get; set; }

    public string? MsgStr { get; set; }

    [Column(TypeName = "numeric(2,0)")]
    public decimal? RetryCnt { get; set; }

    // 0=Pending  1=Dispatched  2=Acknowledged  9=Failed
    [Column(TypeName = "numeric(1,0)")]
    public decimal? TrnStat { get; set; }
    
    public DateTime? CreatedAt { get; set; }

    // Which device this row was dispatched to (MD5 of MAC+IP, first 12 chars)
    [StringLength(32)]
    public string? TypeMID { get; set; }

    // Exact UTC time this row was dispatched (TrnStat flipped 0→1)
    // Used to calculate ACK delay: AckReceivedAt - DispatchedAt
    public DateTime? DispatchedAt { get; set; }
}

// ── Result objects returned from Repository ──────────────────────────────────
// These carry richer data back to the controller for logging purposes.


//evnt table

[Table("Mat_DeviceEvent")]
public class MatDeviceEvent
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column(TypeName = "numeric(18,0)")]
    public decimal EventID { get; set; }

    [Column(TypeName = "numeric(5,0)")]
    public decimal DeviceID { get; set; }

    [Column(TypeName = "numeric(2,0)")]
    public decimal? DeviceType { get; set; }

    public string? Message { get; set; }

    [Column(TypeName = "numeric(18,0)")]
    public decimal EventSeqNo { get; set; }

    public DateTime? Timestamp { get; set; }
}


public class AckResult
{
    // How many rows were actually updated to TrnStat=2
    public int UpdatedCount { get; set; }

    // TrnIDs the client sent that we could NOT find/update
    // (wrong TypeMID, already ACKed, or never existed)
    public List<decimal> MismatchedIds { get; set; } = new();

    // Per-row ACK delay in seconds (TrnID → delay)
    public Dictionary<decimal, double> AckDelays { get; set; } = new();
}

public class StalledGroup
{
    public string  TypeMID    { get; set; } = string.Empty;
    public int     RowCount   { get; set; }
    public int     MaxRetry   { get; set; }
    public int     ResetCount { get; set; }
    public int     FailedCount { get; set; }
}
