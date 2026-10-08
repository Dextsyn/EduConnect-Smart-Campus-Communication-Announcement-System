using EduConnect.Web.Data;
using EduConnect.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace EduConnect.Web.Services
{
    public class ReportingSanctionService : IReportingSanctionService
    {
        private const int SuspensionDays = 7;
        private const int LongSuspensionDays = 30;
        private const int ReferralStrike = 3;

        private readonly ApplicationDbContext _context;
        private readonly INotificationService _notifications;
        private readonly IEmailService _email;
        private readonly IAuditService _audit;

        public ReportingSanctionService(
            ApplicationDbContext context,
            INotificationService notifications,
            IEmailService email,
            IAuditService audit)
        {
            _context = context;
            _notifications = notifications;
            _email = email;
            _audit = audit;
        }

        public async Task<ReportingSanction?> GetActiveSuspensionAsync(int userId)
        {
            var now = DateTime.Now;
            return await _context.ReportingSanctions
                .Where(s => s.UserID == userId &&
                            s.Type == ReportingSanction.Suspension &&
                            s.RevokedAt == null &&
                            s.EndsAt > now)
                .OrderByDescending(s => s.EndsAt)
                .FirstOrDefaultAsync();
        }

        private Task<int> StrikesAsync(int userId)
        {
            var since = DateTime.Now.AddMonths(-12);
            return _context.ReportingSanctions.CountAsync(s =>
                s.UserID == userId && s.RevokedAt == null && s.IssuedAt > since);
        }

        // The ladder: what the given strike earns.
        private static (string Type, int? Days, bool Refer) Step(int strike) => strike switch
        {
            1 => (ReportingSanction.Warning, null, false),
            2 => (ReportingSanction.Suspension, SuspensionDays, false),
            _ => (ReportingSanction.Suspension, LongSuspensionDays, strike >= ReferralStrike)
        };

        private static string Describe(int strike)
        {
            var (type, days, refer) = Step(strike);
            return type == ReportingSanction.Warning
                ? "a warning"
                : $"a {days}-day suspension from filing safety reports" +
                  (refer ? " and a referral to the Office of Student Affairs" : "");
        }

        private static string Ordinal(int n) =>
            n + ((n % 100) is 11 or 12 or 13 ? "th"
                : (n % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" });

        // A legacy anonymous report was filed under a promise of anonymity,
        // and only students are sanctioned.
        private async Task<User?> SanctionableReporterAsync(IncidentReport report)
        {
            if (report.ReportedByID is not int reporterId || report.IsAnonymous)
                return null;

            var reporter = await _context.Users
                .Include(u => u.Role)
                .FirstOrDefaultAsync(u => u.UserID == reporterId);
            return reporter?.Role.RoleName == RoleNames.Student ? reporter : null;
        }

        public async Task<FalseReportPreview> PreviewFalseReportAsync(int reportId)
        {
            var report = await _context.IncidentReports.FindAsync(reportId);
            if (report == null || report.Status == IncidentReport.FalseReport ||
                await _context.ReportingSanctions.AnyAsync(s => s.ReportID == reportId))
                return new FalseReportPreview(false, false, 0, "");

            var reporter = await SanctionableReporterAsync(report);
            if (reporter == null)
                return new FalseReportPreview(true, false, 0, report.IsAnonymous
                    ? "This report was filed anonymously, so no warning or suspension will be given."
                    : "The reporter isn't a student, so no warning or suspension will be given.");

            var strike = await StrikesAsync(reporter.UserID) + 1;
            return new FalseReportPreview(true, true, strike,
                $"This will be {reporter.FirstName} {reporter.LastName}'s {Ordinal(strike)} false report " +
                $"in the last 12 months: {Describe(strike)}.");
        }

        public async Task<SanctionResult> MarkFalseAsync(int reportId, string reason, int staffId)
        {
            if (string.IsNullOrWhiteSpace(reason))
                return SanctionResult.Fail("Say why the report is false; the student will see it.");
            reason = reason.Trim();

            var report = await _context.IncidentReports.FindAsync(reportId);
            if (report == null)
                return SanctionResult.Fail("Report not found.");
            if (report.Status == IncidentReport.FalseReport)
                return SanctionResult.Fail("This report is already marked as false.");
            if (await _context.ReportingSanctions.AnyAsync(s => s.ReportID == reportId))
                return SanctionResult.Fail(
                    "An Administrator reversed the false-report decision on this report, so it can't be marked false again.");

            var staffName = await NameOfAsync(staffId);
            var now = DateTime.Now;
            var statusBefore = report.Status;

            _context.IncidentReportActivities.Add(new IncidentReportActivity
            {
                ReportID = report.ReportID,
                ActorID = staffId,
                ActorName = staffName,
                Action = IncidentReportActivity.MarkedFalse,
                FromStatus = statusBefore,
                ToStatus = IncidentReport.FalseReport,
                Note = reason,
                CreatedAt = now
            });
            report.Status = IncidentReport.FalseReport;
            report.HandledByID = staffId;
            report.ResolvedAt = now;

            var reporter = await SanctionableReporterAsync(report);
            ReportingSanction? sanction = null;
            if (reporter != null)
            {
                var strike = await StrikesAsync(reporter.UserID) + 1;
                var (type, days, refer) = Step(strike);

                DateTime? endsAt = null;
                if (days != null)
                {
                    // Back to back with any suspension still running.
                    var running = await GetActiveSuspensionAsync(reporter.UserID);
                    var start = running?.EndsAt > now ? running.EndsAt!.Value : now;
                    endsAt = start.AddDays(days.Value);
                }

                sanction = new ReportingSanction
                {
                    UserID = reporter.UserID,
                    ReportID = report.ReportID,
                    Type = type,
                    Strike = strike,
                    Reason = reason,
                    IssuedByID = staffId,
                    IssuedByName = staffName,
                    IssuedAt = now,
                    EndsAt = endsAt,
                    ReferredToOsa = refer,
                    ReportStatusBefore = statusBefore
                };
                _context.ReportingSanctions.Add(sanction);

                _audit.Record(type, AuditArea.SafetyReports, report.ReportID,
                    $"{type} for {reporter.FirstName} {reporter.LastName} ({Ordinal(strike)} false report in 12 months), " +
                    $"report #{report.ReportID}" +
                    (endsAt == null ? "" : $", until {endsAt:MMM d, yyyy h:mm tt}") +
                    (refer ? ", referred to the Office of Student Affairs" : "") + $": {reason}",
                    newValues: new { sanction.Type, sanction.Strike, sanction.EndsAt, sanction.ReferredToOsa, Reason = reason });
            }
            else
            {
                _audit.Record("MarkFalse", AuditArea.SafetyReports, report.ReportID,
                    $"Marked report #{report.ReportID} as false (no sanction): {reason}");
            }

            await _context.SaveChangesAsync();

            if (report.ReportedByID is int reporterId)
                await _notifications.SendAsync(reporterId, "SafetyReport",
                    sanction == null
                        ? $"Campus staff marked your safety report #{report.ReportID} as a false report"
                        : $"Your safety report #{report.ReportID} was marked as false: {Describe(sanction.Strike)}",
                    "/SafetyReport/MyReports");

            if (sanction != null && reporter != null)
                _ = _email.SendEmailAsync(reporter.Email, $"{reporter.FirstName} {reporter.LastName}",
                    sanction.Type == ReportingSanction.Warning
                        ? "EduConnect: Warning about a false safety report"
                        : "EduConnect: Safety reporting suspended",
                    $"<p>Hello {reporter.FirstName},</p>" +
                    $"<p>Campus staff found that your safety report #{report.ReportID} was a false report:</p>" +
                    $"<blockquote>{System.Net.WebUtility.HtmlEncode(reason)}</blockquote>" +
                    (sanction.Type == ReportingSanction.Warning
                        ? "<p>This is a warning. Further false reports lead to a suspension of your access to Safety Reports.</p>"
                        : $"<p>You can't file new safety reports until <strong>{sanction.EndsAt:MMMM d, yyyy h:mm tt}</strong>. " +
                          "You can still follow your existing reports.</p>") +
                    (sanction.ReferredToOsa ? "<p>This has been referred to the Office of Student Affairs.</p>" : "") +
                    "<p>If you believe this is a mistake, you can appeal from My Safety Reports.</p>");

            return SanctionResult.Success;
        }

        public async Task<SanctionResult> AppealAsync(int sanctionId, int studentId, string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return SanctionResult.Fail("Explain why the decision should be reversed.");
            if (text.Length > 2000)
                return SanctionResult.Fail("Keep the appeal under 2,000 characters.");

            var sanction = await _context.ReportingSanctions
                .Include(s => s.User)
                .FirstOrDefaultAsync(s => s.SanctionID == sanctionId && s.UserID == studentId);
            if (sanction == null)
                return SanctionResult.Fail("Sanction not found.");
            if (sanction.IsRevoked)
                return SanctionResult.Fail("This sanction has already been revoked.");
            if (sanction.AppealedAt != null)
                return SanctionResult.Fail("You have already appealed this decision.");

            sanction.AppealText = text.Trim();
            sanction.AppealedAt = DateTime.Now;

            _audit.Record("Appeal", AuditArea.SafetyReports, sanction.ReportID,
                $"{sanction.User.FirstName} {sanction.User.LastName} appealed the {sanction.Type.ToLowerInvariant()} " +
                $"for report #{sanction.ReportID}.");

            await _context.SaveChangesAsync();

            var adminIds = await _context.Users
                .Where(u => u.IsActive && u.Role.RoleName == RoleNames.Administrator)
                .Select(u => u.UserID)
                .ToListAsync();
            await _notifications.SendToManyAsync(adminIds, "SafetyReport",
                $"{sanction.User.FirstName} {sanction.User.LastName} appealed a false-report {sanction.Type.ToLowerInvariant()}",
                "/Staff/Sanctions");

            return SanctionResult.Success;
        }

        public async Task<SanctionResult> RevokeAsync(int sanctionId, string reason, int adminId)
        {
            if (string.IsNullOrWhiteSpace(reason))
                return SanctionResult.Fail("Give a reason for revoking it; the student will see it.");
            reason = reason.Trim();

            var sanction = await _context.ReportingSanctions
                .Include(s => s.User)
                .Include(s => s.Report)
                .FirstOrDefaultAsync(s => s.SanctionID == sanctionId);
            if (sanction == null)
                return SanctionResult.Fail("Sanction not found.");
            if (sanction.IsRevoked)
                return SanctionResult.Fail("This sanction has already been revoked.");

            var now = DateTime.Now;
            sanction.RevokedAt = now;
            sanction.RevokedByID = adminId;
            sanction.RevokeReason = reason;

            // The report wasn't false after all: back to where it was.
            var report = sanction.Report;
            if (report.Status == IncidentReport.FalseReport)
            {
                var restored = sanction.ReportStatusBefore ?? "Pending";
                _context.IncidentReportActivities.Add(new IncidentReportActivity
                {
                    ReportID = report.ReportID,
                    ActorID = adminId,
                    ActorName = await NameOfAsync(adminId),
                    Action = IncidentReportActivity.FalseReversed,
                    FromStatus = IncidentReport.FalseReport,
                    ToStatus = restored,
                    Note = reason,
                    CreatedAt = now
                });
                report.Status = restored;
                report.ResolvedAt = restored is "Resolved" or "Dismissed" ? report.ResolvedAt : null;
            }

            _audit.Record("Revoke", AuditArea.SafetyReports, report.ReportID,
                $"Revoked the {sanction.Type.ToLowerInvariant()} for {sanction.User.FirstName} {sanction.User.LastName} " +
                $"(report #{report.ReportID}): {reason}");

            await _context.SaveChangesAsync();

            await _notifications.SendAsync(sanction.UserID, "SafetyReport",
                $"The false-report {sanction.Type.ToLowerInvariant()} on your safety report #{report.ReportID} was revoked",
                "/SafetyReport/MyReports");

            return SanctionResult.Success;
        }

        public async Task<SanctionResult> UpholdAppealAsync(int sanctionId, string? note, int adminId)
        {
            var sanction = await _context.ReportingSanctions
                .Include(s => s.User)
                .FirstOrDefaultAsync(s => s.SanctionID == sanctionId);
            if (sanction == null)
                return SanctionResult.Fail("Sanction not found.");
            if (!sanction.AppealPending)
                return SanctionResult.Fail("There is no open appeal on this sanction.");

            sanction.AppealUpheldAt = DateTime.Now;
            sanction.AppealUpheldByID = adminId;
            sanction.AppealUpheldNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();

            _audit.Record("UpholdAppeal", AuditArea.SafetyReports, sanction.ReportID,
                $"Upheld the {sanction.Type.ToLowerInvariant()} for {sanction.User.FirstName} {sanction.User.LastName} " +
                $"on appeal (report #{sanction.ReportID})" +
                (sanction.AppealUpheldNote == null ? "." : $": {sanction.AppealUpheldNote}"));

            await _context.SaveChangesAsync();

            await _notifications.SendAsync(sanction.UserID, "SafetyReport",
                $"Your appeal on safety report #{sanction.ReportID} was reviewed: the decision stands",
                "/SafetyReport/MyReports");

            return SanctionResult.Success;
        }

        private async Task<string?> NameOfAsync(int userId) =>
            await _context.Users
                .Where(u => u.UserID == userId)
                .Select(u => u.FirstName + " " + u.LastName)
                .FirstOrDefaultAsync();
    }
}
