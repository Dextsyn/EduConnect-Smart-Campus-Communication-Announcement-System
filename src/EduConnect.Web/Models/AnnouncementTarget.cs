using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EduConnect.Web.Models
{
    // Who an announcement is for in the academic hierarchy. Exactly one
    // level per row (check constraint), stored at the level the author
    // chose so later-added programs of a targeted college are included.
    [Table("AnnouncementTargets")]
    public class AnnouncementTarget
    {
        [Key]
        public int AnnouncementTargetID { get; set; }

        [Required]
        public int AnnouncementID { get; set; }

        public int? CollegeID { get; set; }
        public int? DepartmentID { get; set; }
        public int? ProgramID { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public Announcement Announcement { get; set; } = null!;
        public College? College { get; set; }
        public Department? Department { get; set; }
        public AcademicProgram? AcademicProgram { get; set; }
    }
}
