using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace COSEC_demo.Entities
{
    [Table("Mat_DeviceEvent", Schema = "dbo")]
    public class DeviceEvent
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("EventID", TypeName = "numeric(18,0)")]
        public decimal EventID { get; set; }

        [Column("DeviceID", TypeName = "numeric(5,0)")]
        public decimal DeviceID { get; set; }

        [Column("DeviceType", TypeName = "numeric(2,0)")]
        public decimal? DeviceType { get; set; }

        [Column("Message")]
        public string? Message { get; set; }

        [Column("EventSeqNo", TypeName = "numeric(18,0)")]
        public decimal EventSeqNo { get; set; }

        [Column("Timestamp")]
        public DateTime? Timestamp { get; set; }
    }
}
