using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EduConnect.Web.Models
{
    [Table("Announcements")]
    public class Announcement
    {
        [Key]
        public int AnnouncementID { get; set; }

        [Required]
        public int AuthorID { get; set; }

        [Required]
        public int CategoryID { get; set; }

        [Required]
        [MaxLength(20)]
        public string FeedType { get; set; } = "Academic";

        [Required]
        [MaxLength(150)]
        public string Title { get; set; }

        [Required]
        public string Body { get; set; }

        [MaxLength(255)]
        public string? AISummary { get; set; }

        [Required]
        [MaxLength(20)]
        public string Status { get; set; } = "Draft";
        public string ApprovalStatus { get; set; } = "Draft"; // Renamed from 'ApporvalStatus'
        public int? ApprovedByID { get; set; }
        public DateTime? ApprovedAt { get; set; }
        public DateTime? SubmittedAt { get; set; }
        public string? RejectionReason { get; set; }
        public string? ChairRejectionReason { get; set; }
        public int? ChairApprovedByID { get; set; }
        public DateTime? ChairApprovedAt { get; set; }

        [Required]
        public byte Priority { get; set; } = 1;

        [MaxLength(500)]
        public string? AttachmentURL { get; set; }

        public DateTime? PublishedAt { get; set; }
        public DateTime? ExpiresAt { get; set; }

        public bool IsEmergency { get; set; } = false;
        public int ViewCount { get; set; } = 0;

        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime? UpdatedAt { get; set; }

        // Navigation Properties
        public User Author { get; set; }
        public User? ApprovedBy { get; set; }
        public User? ChairApprovedBy { get; set; }
        public AnnouncementCategory Category { get; set; }
        public ICollection<AnnouncementTag> AnnouncementTags { get; set; }
        public ICollection<Notification> Notifications { get; set; }
        public ICollection<Feedback> Feedbacks { get; set; }
        public ICollection<Event> Events { get; set; }
        public ICollection<AIProcessingLog> AIProcessingLogs { get; set; }
        public ICollection<UserAnnouncementInteraction> UserInteractions { get; set; }
    }
}