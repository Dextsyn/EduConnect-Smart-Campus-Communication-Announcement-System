using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EduConnect.Web.Models
{
    [Table("Feedback")]
    public class Feedback
    {
        [Key]
        public int FeedbackID { get; set; }

        [Required]
        public int AnnouncementID { get; set; }

        [Required]
        public int UserID { get; set; }

        [MaxLength(1000)]
        public string? FeedbackText { get; set; }

        public byte? Rating { get; set; }

        [Column(TypeName = "decimal(4,3)")]
        public decimal? SentimentScore { get; set; }

        [MaxLength(20)]
        public string? SentimentLabel { get; set; }

        public bool IsAcknowledged { get; set; } = false;

        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime? UpdatedAt { get; set; }

        // Navigation Properties
        public Announcement Announcement { get; set; }
        public User User { get; set; }
    }
}