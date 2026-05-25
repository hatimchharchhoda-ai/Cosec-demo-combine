using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NMatGen.API.Models
{
    [Table("Mat_UserMst", Schema = "dbo")]
    public class MatUserMst
    {
        [Key]
        [Column("UserID")]
        [StringLength(10)]
        public string UserId { get; set; } = string.Empty;

        [Column("UserName")]
        [StringLength(200)]
        public string? UserName { get; set; }

        [Column("IsActive", TypeName = "numeric(1,0)")]
        public decimal? IsActive { get; set; }

        [Column("UserShortName")]
        [StringLength(16)]
        public string? UserShortName { get; set; }

        [Column("UserIDN", TypeName = "numeric(20,0)")]
        public decimal? UserIDN { get; set; }
    }
}