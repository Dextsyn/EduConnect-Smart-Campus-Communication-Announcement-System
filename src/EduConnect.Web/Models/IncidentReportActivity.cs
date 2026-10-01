using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EduConnect.Web.Models
{
    // Append-only history of a safety report: one row when it is received
    // and one per change a staff member makes. Rows are never updated or
    // deleted, so the work done on a report outlives later overwrites of
    // IncidentReport.Status/Resolution.
    [Table("IncidentReportActivities")]
    public class IncidentReportActivity
    {
        public const string Received = "Received";
        public const string StatusChanged = "StatusChanged";
        public const string NoteUpdated = "NoteUpdated";

        [Key]
        public int ActivityID { get; set; }

        public int ReportID { get; set; }

        public int? ActorID { get; set; }
        // NULL = a legacy anonymous reporter, or the account was deleted

        [MaxLength(200)]
        public string? ActorName { get; set; }
        // Snapshot, so the log stays readable after the account is gone

        [Required]
        [MaxLength(20)]
        public string Action { get; set; }

        [MaxLength(20)]
        public string? FromStatus { get; set; }

        [MaxLength(20)]
        public string? ToStatus { get; set; }

        public string? Note { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        [NotMapped]
        public string Summary => Action switch
        {
            Received      => "Report received",
            StatusChanged => $"{FromStatus} → {ToStatus}",
            NoteUpdated   => "Note updated",
            _             => Action
        };

        // Reports filed while anonymous reporting existed logged their
        // reporter as "Anonymous", but the report itself always kept who
        // filed it. Load Report.ReportedBy to show that name instead.
        [NotMapped]
        public string? DisplayActorName =>
            ActorName == "Anonymous" && Report?.ReportedBy != null
                ? $"{Report.ReportedBy.FirstName} {Report.ReportedBy.LastName}"
                : ActorName;

        // Navigation
        public IncidentReport Report { get; set; }
        public User? Actor { get; set; }
    }
}
