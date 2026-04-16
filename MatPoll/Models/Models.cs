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

    // NEW: TypeMID = hash of MAC+IP — identifies which device this row belongs to
    // Generated at dispatch time. Used for filtering, restore, and logging.
    [StringLength(32)]
    public string? TypeMID { get; set; }
}
