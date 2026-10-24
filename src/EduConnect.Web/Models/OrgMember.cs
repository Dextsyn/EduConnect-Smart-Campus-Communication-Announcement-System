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
        // Values: OrgRoles.Member, OrgRoles.Officer (a Student who posts
        // for the org), OrgRoles.Adviser (retired; kept inactive)

        [MaxLength(100)]
        public string? Position { get; set; }
        // An officer's title, e.g. "President", "PRO". A label only —
        // every officer has the same rights.

        public bool IsActive { get; set; } = true;

        public DateTime JoinedAt { get; set; }
            = DateTime.Now;

        // Navigation
        public Organization Organization { get; set; }
        public User User { get; set; }
    }
}