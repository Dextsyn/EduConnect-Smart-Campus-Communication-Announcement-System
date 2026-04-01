using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EduConnect.Web.Models
{
    [Table("RefreshTokens")]
    public class RefreshToken
    {
        [Key]
        public int TokenID { get; set; }

        [Required]
        public int UserID { get; set; }

        [Required]
        [MaxLength(512)]
        public string Token { get; set; }

        [Required]
        public DateTime ExpiresAt { get; set; }

        public bool IsRevoked { get; set; } = false;

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        [MaxLength(255)]
        public string? DeviceInfo { get; set; }

        // Navigation Properties
        public User User { get; set; }
    }
}