using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EduConnect.Web.Models
{
    [Table("AIProcessingLogs")]
    public class AIProcessingLog
    {
        [Key]
        public int AILogID { get; set; }

        public int? AnnouncementID { get; set; }

        [Required]
        [MaxLength(50)]
        public string ProcessType { get; set; }

        public string? InputText { get; set; }
        public string? OutputText { get; set; }

        [MaxLength(100)]
        public string? ModelUsed { get; set; }

        public int? TokensUsed { get; set; }

        [Column(TypeName = "decimal(5,4)")]
        public decimal? Confidence { get; set; }

        public DateTime ProcessedAt { get; set; } = DateTime.Now;

        // Navigation Properties
        public Announcement? Announcement { get; set; }
    }
}