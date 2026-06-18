using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EduConnect.Web.Models
{
    [Table("AnnouncementCategories")]
    public class AnnouncementCategory
    {
        [Key]
        public int CategoryID { get; set; }

        [Required]
        [MaxLength(100)]
        public string CategoryName { get; set; }

        [MaxLength(255)]
        public string? Description { get; set; }

        [Required]
        [MaxLength(7)]
        public string ColorHex { get; set; } = "#000000";

        [MaxLength(50)]
        public string? IconName { get; set; }

        public bool IsEmergency { get; set; } = false;

        [Required]
        [MaxLength(20)]
        public string FeedType { get; set; } = "NonAcademic";

        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime? UpdatedAt { get; set; }

        // Navigation Properties
        public ICollection<Announcement> Announcements { get; set; }
    }
}