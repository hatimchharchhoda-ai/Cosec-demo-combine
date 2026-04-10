using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace COSEC_demo.Entities
{
    [Table("Mat_LoginUserMst", Schema = "dbo")]
    public class LoginUser
    {
        [Key]
        public string LoginUserID { get; set; }
        public string LoginPassword { get; set; }
        public decimal IsActive { get; set; }
    }
}
