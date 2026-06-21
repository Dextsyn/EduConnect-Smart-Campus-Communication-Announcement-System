using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EduConnect.Web.Models
{
    [Table("IncidentReports")]
    public class IncidentReport
    {
        [Key]
        public int ReportID { get; set; }

        public int? ReportedByID { get; set; }
        // NULL = anonymous report

        [Required]
        [MaxLength(50)]
        public string IncidentType { get; set; }
        // Values: Safety, Facility, Harassment,
        //         Suspicious, Medical, Other

        [Required]
        public string Description { get; set; }

        [MaxLength(255)]
        public string? Location { get; set; }

        [MaxLength(500)]
        public string? PhotoURL { get; set; }

        [MaxLength(20)]
        public string Status { get; set; } = "Pending";
        // Values: Pending, Investigating,
        //         Resolved, Dismissed

        public int? HandledByID { get; set; }

        public string? Resolution { get; set; }

        public bool IsAnonymous { get; set; } = false;

        public DateTime ReportedAt { get; set; }
            = DateTime.Now;

        public DateTime? ResolvedAt { get; set; }

        // Navigation
        public User? ReportedBy { get; set; }
        public User? HandledBy { get; set; }
    }
}