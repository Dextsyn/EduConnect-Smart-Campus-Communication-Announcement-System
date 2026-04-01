using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EduConnect.Web.Models
{
    [Table("AnnouncementTags")]
    public class AnnouncementTag
    {
        [Key]
        public int AnnouncementTagID { get; set; }

        [Required]
        public int AnnouncementID { get; set; }

        [Required]
        public int TagID { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        // Navigation Properties
        public Announcement Announcement { get; set; }
        public DepartmentTag DepartmentTag { get; set; }
    }
}