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

        public decimal TrnStat { get; set; }

        public DateTime CreatedAt { get; set; }
    }
}
