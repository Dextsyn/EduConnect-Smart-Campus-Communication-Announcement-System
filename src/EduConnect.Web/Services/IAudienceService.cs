using System.Linq.Expressions;
using EduConnect.Web.Models;
using EduConnect.Web.ViewModels;

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

    // The part of the hierarchy an author may pick from. College holds only
    // the departments and programs they may target.
    public class TargetOptions
    {
        public College? College { get; set; }
        public bool CanTargetCollege { get; set; }
        public bool CanTargetDepartments { get; set; }
    }

    public record TargetSelection(
        IReadOnlyList<int> CollegeIDs,
        IReadOnlyList<int> DepartmentIDs,
        IReadOnlyList<int> ProgramIDs)
    {
        public bool IsEmpty =>
            CollegeIDs.Count == 0 && DepartmentIDs.Count == 0 && ProgramIDs.Count == 0;
    }

    public interface IAudienceService
    {
        Task<Viewer> GetViewerAsync(int userId);

        // Feed scope: School Wide, the viewer's tags, their own posts, and
        // targets that reach them.
        Expression<Func<Announcement, bool>> VisibleTo(Viewer viewer);

        // VisibleTo without School Wide or authorship: what is specifically
        // for the viewer's program/department/college or tags.
        Expression<Func<Announcement, bool>> AddressedTo(Viewer viewer);

        // Dean: own college; Chairperson: own department; Faculty: own
        // department's programs only. Unplaced authors: none.
        Task<TargetOptions> GetTargetOptionsAsync(int authorId);

        // Refuses any posted target outside GetTargetOptionsAsync.
        Task<HierarchyResult> ValidateTargetsAsync(int authorId, TargetSelection selection);

        // Active users an announcement reaches, never including
        // excludeUserId (the author).
        Task<List<int>> GetRecipientIdsAsync(int announcementId, int excludeUserId);

        // Appends each row's target short labels to its Tags.
        Task AddTargetLabelsAsync(IEnumerable<AnnouncementTableViewModel> rows);

        // Full names of an announcement's targets, for the details page.
        Task<List<string>> GetTargetNamesAsync(int announcementId);
    }
}
