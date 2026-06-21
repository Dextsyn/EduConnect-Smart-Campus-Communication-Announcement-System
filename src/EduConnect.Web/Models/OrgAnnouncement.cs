using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EduConnect.Web.Models
{
    [Table("OrgAnnouncements")]
    public class OrgAnnouncement
    {
        [Key]
        public int OrgAnnouncementID { get; set; }

        [Required]
        public int OrgID { get; set; }

        [Required]
        public int PostedByID { get; set; }

        [Required]
        [MaxLength(300)]
        public string Title { get; set; }

        [Required]
        public string Body { get; set; }

        [MaxLength(500)]
        public string? AttachmentURL { get; set; }

        public bool IsPinned { get; set; } = false;

        public DateTime? ExpiresAt { get; set; }

        public DateTime PostedAt { get; set; }
            = DateTime.Now;

        public DateTime? UpdatedAt { get; set; }

        // Navigation
        public Organization Organization { get; set; }
        public User PostedBy { get; set; }
    }
}