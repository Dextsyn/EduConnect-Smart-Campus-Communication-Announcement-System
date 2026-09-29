using EduConnect.Web.Models;

namespace EduConnect.Web.Services
{
    public record ApprovalRouting(bool Ok, string? Error, string Status, List<User> Reviewers)
    {
        public static ApprovalRouting Fail(string error) => new(false, error, "", new List<User>());
    }

    // Who reviews an announcement next, and which pending announcements a
    // reviewer may act on. Routing follows the author's placement.
    public interface IApprovalService
    {
        // deanOnly: skip the Chairperson (escalation, or a Chairperson's own
        // post). Otherwise: the author's department's Chairpersons, falling
        // back to the college's Deans.
        Task<ApprovalRouting> RouteAsync(int authorId, bool deanOnly);

        // PendingChair in the Chairperson's department, or PendingDean in
        // the Dean's college. Empty for everyone else.
        IQueryable<Announcement> ReviewableBy(Viewer reviewer);
    }
}
