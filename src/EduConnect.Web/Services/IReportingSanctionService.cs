using EduConnect.Web.Models;

namespace EduConnect.Web.Services
{
    public record SanctionResult(bool Ok, string? Error)
    {
        public static readonly SanctionResult Success = new(true, null);
        public static SanctionResult Fail(string error) => new(false, error);
    }

    // What marking a report false would do, for staff to see first.
    // Sanctions: whether the reporter gets a warning or suspension at all
    // (students only, never a legacy anonymous report).
    public record FalseReportPreview(bool CanMark, bool Sanctions, int Strike, string Summary);

    // False safety reports and what they cost the student who filed them.
    // Each report staff mark as false is one strike; strikes in the last
    // 12 months (revoked ones don't count) set the sanction:
    //   1st: a warning
    //   2nd: 7 days without filing new reports
    //   3rd and later: 30 days, and a referral to the Office of Student
    //   Affairs (flagged here, handled outside EduConnect)
    // A suspension expires by itself and only blocks filing new reports;
    // the student always keeps seeing their reports and sanctions. A student
    // may appeal each sanction once; an Administrator upholds or revokes it.
    public interface IReportingSanctionService
    {
        Task<ReportingSanction?> GetActiveSuspensionAsync(int userId);

        Task<FalseReportPreview> PreviewFalseReportAsync(int reportId);

        Task<SanctionResult> MarkFalseAsync(int reportId, string reason, int staffId);

        Task<SanctionResult> AppealAsync(int sanctionId, int studentId, string text);

        Task<SanctionResult> RevokeAsync(int sanctionId, string reason, int adminId);

        Task<SanctionResult> UpholdAppealAsync(int sanctionId, string? note, int adminId);
    }
}
