using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace COSEC_demo.Entities
{
    [Table("Mat_DeviceMst", Schema = "dbo")]
    public class Device
    {
        [Key]
        [Column("DeviceID")]
        public decimal DeviceID { get; set; }

        [Column("DeviceName")]
        public string? DeviceName { get; set; }

        [Column("MACAddr")]
        public string? MACAddr { get; set; }

        [Column("IPAddr")]
        public string? IPAddr { get; set; }

        [Column("IsActive")]
        public decimal IsActive { get; set; }

        [Column("DeviceType")]
        public decimal DeviceType { get; set; }

        [Column("LastSeenAt")]
        public DateTime? LastSeenAt { get; set; }

        [Column("IsOnline")]
        public bool IsOnline { get; set; }

        [Column("OfflineSince")]
        public DateTime? OfflineSince { get; set; }
    }
}