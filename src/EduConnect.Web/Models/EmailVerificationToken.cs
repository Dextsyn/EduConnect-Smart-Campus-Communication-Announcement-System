using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EduConnect.Web.Models
{
    // A link emailed to a self-registered student to prove they own the
    // address. Only the SHA-256 of the token is stored; the token itself
    // exists only in the email.
    [Table("EmailVerificationTokens")]
    public class EmailVerificationToken
    {
        [Key]
        public int TokenID { get; set; }

        [Required]
        public int UserID { get; set; }

        [Required]
        [MaxLength(64)]
        public string TokenHash { get; set; }

        [Required]
        public DateTime ExpiresAt { get; set; }

        public bool IsUsed { get; set; } = false;

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public User User { get; set; }
    }
}
