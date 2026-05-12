using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace COSEC_demo.Entities
{
    [Table("Mat_CommTrn", Schema = "dbo")]
    public class CommTrn
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("TrnID")]
        public decimal TrnID { get; set; } 

        [Column("MsgStr")]
        public string? MsgStr { get; set; }

        [Column("RetryCnt")]
        public decimal? RetryCnt { get; set; }

        [Column("TrnStat")]
        public decimal? TrnStat { get; set; }

        [Column("CreatedAt")]
        public DateTime? CreatedAt { get; set; }

        [Column("DispatchedAt")]
        public DateTime? DispatchedAt { get; set; }

        [Column("DeviceType")]
        public decimal? DeviceType { get; set; }

        [Column("DeviceID")]
        public decimal? DeviceID { get; set; }
    }
}