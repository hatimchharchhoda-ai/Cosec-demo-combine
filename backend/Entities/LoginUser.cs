using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace COSEC_demo.Entities
{
    [Table("Mat_LoginUserMst", Schema = "dbo")]
    public class LoginUser
    {
        [Key]
        [Column("LoginUserID")]
        public string LoginUserID { get; set; }

        [Column("LoginPassword")]
        public string LoginPassword { get; set; }

        [Column("IsActive")]
        public decimal IsActive { get; set; }
    }
}