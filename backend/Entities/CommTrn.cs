using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace COSEC_demo.Entities
{
    [Table("Mat_CommTrn", Schema = "dbo")]
    public class CommTrn
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("TrnID", TypeName = "numeric(18,0)")]
        public decimal TrnID { get; set; } 

        [Column("MsgStr")]
        public string? MsgStr { get; set; }

        [Column("RetryCnt", TypeName = "numeric(2,0)")]
        public decimal? RetryCnt { get; set; }

        [Column("TrnStat", TypeName = "numeric(1,0)")]
        public decimal? TrnStat { get; set; }

        [Column("CreatedAt")]
        public DateTime? CreatedAt { get; set; }

        [Column("DispatchedAt")]
        public DateTime? DispatchedAt { get; set; }

        [Column("DeviceType", TypeName = "numeric(18,0)")]
        public decimal? DeviceType { get; set; }

        [Column("DeviceID", TypeName = "numeric(5,0)")]
        public decimal? DeviceID { get; set; }
    }
}