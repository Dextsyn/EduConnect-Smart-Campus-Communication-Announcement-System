using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EduConnect.Web.Models
{
    [Table("OrgMembers")]
    public class OrgMember
    {
        [Key]
        public int MemberID { get; set; }

        [Required]
        public int OrgID { get; set; }

        [Required]
        public int UserID { get; set; }

        [Required]
        [MaxLength(20)]
        public string OrgRole { get; set; } = "Member";
        // Values: Member, Officer, President, Adviser

        [MaxLength(100)]
        public string? Position { get; set; }
        // e.g. "Vice President", "Secretary"

        public bool IsActive { get; set; } = true;

        public DateTime JoinedAt { get; set; }
            = DateTime.Now;

        // Navigation
        public Organization Organization { get; set; }
        public User User { get; set; }
    }
}