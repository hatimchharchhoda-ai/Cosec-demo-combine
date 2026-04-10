using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NMatGen.API.Models
{ 

    [Table("Mat_UserMst")]
    public class MatUserMst
    {
        [Key]
        public string UserId { get; set; } = string.Empty;
        public string? UserName { get; set; }
        public decimal isActive { get; set; }
        public string? UserShortName { get; set; }
        public decimal? UserIDN { get; set; }
    }
}