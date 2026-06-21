using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EduConnect.Web.Models
{
    [Table("StudyGroupMembers")]
    public class StudyGroupMember
    {
        [Key]
        public int MembershipID { get; set; }

        [Required]
        public int GroupID { get; set; }

        [Required]
        public int UserID { get; set; }

        public DateTime JoinedAt { get; set; }
            = DateTime.Now;

        public DateTime? AttendedAt { get; set; }

        // Navigation
        public StudyGroup StudyGroup { get; set; }
        public User User { get; set; }
    }
}