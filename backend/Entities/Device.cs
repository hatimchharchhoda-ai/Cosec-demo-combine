using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace COSEC_demo.Entities
{
    [Table("Mat_DeviceMst", Schema = "dbo")]
    public class Device
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.None)]
        [Column("DeviceID", TypeName = "numeric(5,0)")]
        public decimal DeviceID { get; set; }

        [Column("DeviceName")]
        [StringLength(200)]
        public string? DeviceName { get; set; }

        [Column("MACAddr")]
        [StringLength(50)]
        public string? MACAddr { get; set; }

        [Column("IPAddr")]
        [StringLength(50)]
        public string? IPAddr { get; set; }

        [Column("IsActive", TypeName = "numeric(1,0)")]
        public decimal? IsActive { get; set; }

        [Column("DeviceType", TypeName = "numeric(2,0)")]
        public decimal? DeviceType { get; set; }

        [Column("LastSeenAt")]
        public DateTime? LastSeenAt { get; set; }

        [Column("IsOnline")]
        public bool IsOnline { get; set; }

        [Column("OfflineSince")]
        public DateTime? OfflineSince { get; set; }
    }
}