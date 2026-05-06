using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace COSEC_demo.Entities
{
    [Table("Mat_CommTrn", Schema = "dbo")]
    public class CommTrn
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public decimal TrnID { get; set; }

        public string MsgStr { get; set; }
        public decimal RetryCnt { get; set; }

        // 0=Pending 1=Dispatched 2=Acknowledged 9=Failed
        public decimal TrnStat { get; set; }

        public DateTime CreatedAt { get; set; }
        public int DeviceID { get; set; }
        public int DeviceType { get; set; }

        // New fields
        public DateTime? DispatchedAt { get; set; }
    }
}