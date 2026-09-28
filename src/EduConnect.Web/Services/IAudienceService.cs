using System.Linq.Expressions;
using EduConnect.Web.Models;

namespace EduConnect.Web.Services
{
    // Everything that decides an announcement's audience: who reads it,
    // who is notified, and what an author may target.
    public record Viewer(
        int UserID,
        string RoleName,
        int? CollegeID,
        int? DepartmentID,
        int? ProgramID,
        IReadOnlyList<int> TagIDs);

    public interface IAudienceService
    {
        Task<Viewer> GetViewerAsync(int userId);

        // Feed scope: School Wide, the viewer's tags, their own posts, and
        // targets that reach them.
        Expression<Func<Announcement, bool>> VisibleTo(Viewer viewer);

        // VisibleTo without School Wide or authorship: what is specifically
        // for the viewer's program/department/college or tags.
        Expression<Func<Announcement, bool>> AddressedTo(Viewer viewer);
    }
}
