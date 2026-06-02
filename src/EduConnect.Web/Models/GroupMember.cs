using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EduConnect.Web.Models
{
    [Table("GroupMembers")]
    public class GroupMember
    {
        [Key]
        public int MembershipID { get; set; }

        [Required]
        public int GroupID { get; set; }

        [Required]
        public int UserID { get; set; }

        public DateTime JoinedAt { get; set; } = DateTime.UtcNow;

        public Group Group { get; set; }
        public User User { get; set; }
    }
}
