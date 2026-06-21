using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EduConnect.Web.Models
{
    [Table("UserAnnouncementInteractions")]
    public class UserAnnouncementInteraction
    {
        [Key]
        public int InteractionID { get; set; }

        [Required]
        public int UserID { get; set; }

        [Required]
        public int AnnouncementID { get; set; }

        public DateTime ViewedAt { get; set; } = DateTime.Now;

        public User User { get; set; }
        public Announcement Announcement { get; set; }
    }
}
