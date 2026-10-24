using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EduConnect.Web.Models
{
    // A student who follows an organization to see its posts. Separate from
    // OrgMembers: following is self-service and never makes a student a
    // member or eligible to be an officer. Unfollowing deletes the row.
    [Table("OrgFollowers")]
    public class OrgFollower
    {
        [Key]
        public int OrgFollowerID { get; set; }

        [Required]
        public int OrgID { get; set; }

        [Required]
        public int UserID { get; set; }

        public DateTime FollowedAt { get; set; }
            = DateTime.Now;

        // Navigation
        public Organization Organization { get; set; }
        public User User { get; set; }
    }
}
