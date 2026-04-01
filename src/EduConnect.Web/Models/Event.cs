using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EduConnect.Web.Models
{
    [Table("Events")]
    public class Event
    {
        [Key]
        public int EventID { get; set; }

        public int? AnnouncementID { get; set; }

        [Required]
        public int OrganizerID { get; set; }

        [Required]
        [MaxLength(255)]
        public string EventTitle { get; set; }

        [MaxLength(255)]
        public string? Location { get; set; }

        [Required]
        public DateTime StartDateTime { get; set; }

        [Required]
        public DateTime EndDateTime { get; set; }

        public int? MaxAttendees { get; set; }
        public bool IsOnline { get; set; } = false;

        [MaxLength(500)]
        public string? MeetingURL { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime? UpdatedAt { get; set; }

        // Navigation Properties
        public Announcement? Announcement { get; set; }
        public User Organizer { get; set; }
    }
}