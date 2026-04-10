using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace COSEC_demo.Entities
{
    [Table("Mat_DeviceMst", Schema = "dbo")]
    public class Device
    {
        [Key]
        public decimal DeviceID { get; set; }

        public string DeviceName { get; set; }
        public string MACAddr { get; set; }
        public string IPAddr { get; set; }

        public decimal IsActive { get; set; }

        public decimal DeviceType { get; set; }
    }
}
