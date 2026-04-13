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

    // 0 = inactive,  1 = active
    [Column(TypeName = "numeric(1,0)")]
    public decimal? IsActive { get; set; }

    // numeric(2,0)  — your system stores device type as a number
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

    // 0 = inactive,  1 = active
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

    // timestamp is auto-managed by SQL Server — EF maps it as byte[]
    [Timestamp]
    //public byte[]? Time { get; set; }

    public DateTime? CreatedAt { get; set; }
}
