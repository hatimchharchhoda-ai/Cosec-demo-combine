using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NMatGen.API.Models
{
    [Table("Mat_UserMst", Schema = "dbo")]
    public class MatUserMst
    {
        [Key]
        [Column("UserID")]
        public string UserId { get; set; } = string.Empty;

        [Column("UserName")]
        public string? UserName { get; set; }

        [Column("IsActive")]
        public decimal IsActive { get; set; }

        [Column("UserShortName")]
        public string? UserShortName { get; set; }

        [Column("UserIDN")]
        public decimal? UserIDN { get; set; }
    }
}