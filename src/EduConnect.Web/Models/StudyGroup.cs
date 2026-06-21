using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EduConnect.Web.Models
{
    [Table("StudyGroups")]
    public class StudyGroup
    {
        [Key]
        public int GroupID { get; set; }

        [Required]
        public int CreatedByID { get; set; }

        public int? DepartmentTagID { get; set; }

        [Required]
        [MaxLength(200)]
        public string SubjectName { get; set; }

        [MaxLength(500)]
        public string? Description { get; set; }

        [Required]
        public DateTime ScheduledAt { get; set; }

        [MaxLength(255)]
        public string? Location { get; set; }

        public bool IsOnline { get; set; } = false;

        [MaxLength(500)]
        public string? MeetingURL { get; set; }

        public byte MaxMembers { get; set; } = 10;

        [MaxLength(20)]
        public string Status { get; set; } = "Open";
        // Values: Open, Full, Completed, Cancelled

        public DateTime CreatedAt { get; set; }
            = DateTime.Now;

        // Navigation
        public User CreatedBy { get; set; }
        public DepartmentTag? DepartmentTag { get; set; }
        public ICollection<StudyGroupMember>
            Members
        { get; set; }
    }
}