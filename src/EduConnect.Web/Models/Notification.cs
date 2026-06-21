using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EduConnect.Web.Models
{
    [Table("Notifications")]
    public class Notification
    {
        [Key]
        public int NotificationID { get; set; }

        [Required]
        public int UserID { get; set; }

        public int? AnnouncementID { get; set; }

        [Required]
        [MaxLength(50)]
        public string Type { get; set; } = "Announcement";

        [Required]
        [MaxLength(500)]
        public string Message { get; set; }

        [MaxLength(200)]
        public string? Link { get; set; }

        public bool IsRead { get; set; } = false;
        public DateTime? ReadAt { get; set; }

        [Required]
        [MaxLength(20)]
        public string Channel { get; set; } = "InApp";

        public DateTime SentAt { get; set; } = DateTime.Now;

        // Navigation Properties
        public User User { get; set; }
        public Announcement? Announcement { get; set; }
    }
}