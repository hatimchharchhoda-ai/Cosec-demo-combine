using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace COSEC_demo.Entities
{
    [Table("Mat_LoginUserMst", Schema = "dbo")]
    public class LoginUser
    {
        [Key]
        [Column("LoginUserID")]
        [StringLength(100)]
        public string LoginUserID { get; set; }

        [Column("LoginPassword")]
        [StringLength(500)]
        public string LoginPassword { get; set; }

        [Column("IsActive", TypeName = "numeric(1,0)")]
        public decimal? IsActive { get; set; }
    }
}