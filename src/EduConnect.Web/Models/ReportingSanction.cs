using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EduConnect.Web.Models
{
    // What a student gets when staff mark one of their safety reports as a
    // knowingly false report: one row per false report, a warning or a
    // suspension from filing new reports depending on how many they have
    // had in the last year (IReportingSanctionService). Rows are never
    // deleted; an Administrator can revoke one, which lifts it and stops it
    // counting.
    [Table("ReportingSanctions")]
    public class ReportingSanction
    {
        public const string Warning = "Warning";
        public const string Suspension = "Suspension";

        [Key]
        public int SanctionID { get; set; }

        // The student who filed the false report.
        public int UserID { get; set; }

        // The report staff marked as false.
        public int ReportID { get; set; }

        [Required]
        [MaxLength(20)]
        public string Type { get; set; } = Warning;

        // Which false report in the last 12 months this was (1st, 2nd, ...).
        public int Strike { get; set; }

        // Why staff found the report false. Shown to the student.
        [Required]
        public string Reason { get; set; } = "";

        public int? IssuedByID { get; set; }

        [MaxLength(200)]
        public string? IssuedByName { get; set; }
        // Snapshot, so the record stays readable after the account is gone

        public DateTime IssuedAt { get; set; } = DateTime.Now;

        // Suspensions only: no new reports until then. Expires by itself.
        public DateTime? EndsAt { get; set; }

        // Third strike and later: flagged for the Office of Student Affairs,
        // which is contacted outside EduConnect.
        public bool ReferredToOsa { get; set; }

        // The report's status before it was marked false, restored if the
        // sanction is revoked.
        [MaxLength(20)]
        public string? ReportStatusBefore { get; set; }

        public string? AppealText { get; set; }
        public DateTime? AppealedAt { get; set; }

        public DateTime? RevokedAt { get; set; }
        public int? RevokedByID { get; set; }
        public string? RevokeReason { get; set; }

        // Set when an Administrator decides an appeal and keeps the sanction
        // (revoking one decides its appeal too).
        public DateTime? AppealUpheldAt { get; set; }
        public int? AppealUpheldByID { get; set; }
        public string? AppealUpheldNote { get; set; }

        [NotMapped]
        public bool IsRevoked => RevokedAt != null;

        [NotMapped]
        public bool AppealPending => AppealedAt != null && RevokedAt == null && AppealUpheldAt == null;

        public bool IsActiveSuspensionAt(DateTime now) =>
            Type == Suspension && RevokedAt == null && EndsAt > now;

        // Navigation
        public User User { get; set; }
        public IncidentReport Report { get; set; }
        public User? IssuedBy { get; set; }
        public User? RevokedBy { get; set; }
        public User? AppealUpheldBy { get; set; }
    }
}
