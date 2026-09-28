using System.Linq.Expressions;
using EduConnect.Web.Data;
using EduConnect.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace EduConnect.Web.Services
{
    public class AudienceService : IAudienceService
    {
        private const string SchoolWide = "ALL";

        private readonly ApplicationDbContext _context;

        public AudienceService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<Viewer> GetViewerAsync(int userId)
        {
            var user = await _context.Users
                .Where(u => u.UserID == userId)
                .Select(u => new
                {
                    u.UserID,
                    Role = u.Role.RoleName,
                    u.CollegeID,
                    u.DepartmentID,
                    u.ProgramID
                })
                .FirstOrDefaultAsync();

            if (user == null)
                return new Viewer(userId, "", null, null, null, Array.Empty<int>());

            var tagIds = await _context.UserDepartments
                .Where(ud => ud.UserID == userId)
                .Select(ud => ud.TagID)
                .ToListAsync();

            return new Viewer(user.UserID, user.Role, user.CollegeID,
                user.DepartmentID, user.ProgramID, tagIds);
        }

        public Expression<Func<Announcement, bool>> VisibleTo(Viewer viewer) =>
            Build(viewer, includeSchoolWideAndOwn: true);

        public Expression<Func<Announcement, bool>> AddressedTo(Viewer viewer) =>
            Build(viewer, includeSchoolWideAndOwn: false);

        public async Task<TargetOptions> GetTargetOptionsAsync(int authorId)
        {
            var author = await _context.Users
                .Where(u => u.UserID == authorId)
                .Select(u => new { Role = u.Role.RoleName, u.CollegeID, u.DepartmentID })
                .FirstOrDefaultAsync();

            var options = new TargetOptions();
            if (author?.CollegeID == null)
                return options;

            var isDean = author.Role == RoleNames.Dean;
            var isStaff = author.Role is RoleNames.Chairperson or RoleNames.Faculty;
            if (!isDean && !(isStaff && author.DepartmentID != null))
                return options;

            var college = await _context.Colleges
                .Include(c => c.Departments)
                    .ThenInclude(d => d.Programs)
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.CollegeID == author.CollegeID && c.IsActive);
            if (college == null)
                return options;

            college.Departments = college.Departments
                .Where(d => d.IsActive && (isDean || d.DepartmentID == author.DepartmentID))
                .OrderBy(d => d.Name)
                .ToList();
            foreach (var dept in college.Departments)
                dept.Programs = dept.Programs
                    .Where(p => p.IsActive)
                    .OrderBy(p => p.Name)
                    .ToList();

            options.College = college;
            options.CanTargetCollege = isDean;
            options.CanTargetDepartments = isDean || author.Role == RoleNames.Chairperson;
            return options;
        }

        public async Task<HierarchyResult> ValidateTargetsAsync(int authorId, TargetSelection selection)
        {
            if (selection.IsEmpty)
                return HierarchyResult.Success;

            var options = await GetTargetOptionsAsync(authorId);
            var college = options.College;

            var colleges = options.CanTargetCollege && college != null
                ? new HashSet<int> { college.CollegeID }
                : new HashSet<int>();
            var departments = options.CanTargetDepartments && college != null
                ? college.Departments.Where(d => !d.IsImplicit).Select(d => d.DepartmentID).ToHashSet()
                : new HashSet<int>();
            var programs = college?.Departments.SelectMany(d => d.Programs).Select(p => p.ProgramID).ToHashSet()
                ?? new HashSet<int>();

            if (selection.CollegeIDs.Any(id => !colleges.Contains(id)) ||
                selection.DepartmentIDs.Any(id => !departments.Contains(id)) ||
                selection.ProgramIDs.Any(id => !programs.Contains(id)))
                return HierarchyResult.Fail(
                    "You can only post to your own " +
                    (options.CanTargetCollege ? "college." :
                     options.CanTargetDepartments ? "department and its programs." :
                     "department's programs."));

            return HierarchyResult.Success;
        }

        public static Expression<Func<Announcement, bool>> Not(
            Expression<Func<Announcement, bool>> expression) =>
            Expression.Lambda<Func<Announcement, bool>>(
                Expression.Not(expression.Body), expression.Parameters);

        // One lambda per role so EF translates each to a single WHERE.
        // Captured locals become SQL parameters.
        private static Expression<Func<Announcement, bool>> Build(Viewer v, bool includeSchoolWideAndOwn)
        {
            var tagIds = v.TagIDs.ToList();
            var userId = v.UserID;
            var collegeId = v.CollegeID;
            var departmentId = v.DepartmentID;
            var programId = v.ProgramID;
            var wide = includeSchoolWideAndOwn;

            switch (v.RoleName)
            {
                case RoleNames.Student:
                case RoleNames.StudentPending:
                    return a =>
                        (wide && (a.AuthorID == userId ||
                                  a.AnnouncementTags.Any(t => t.DepartmentTag.ShortName == SchoolWide))) ||
                        a.AnnouncementTags.Any(t => tagIds.Contains(t.TagID)) ||
                        a.AnnouncementTargets.Any(t =>
                            (programId != null && t.ProgramID == programId) ||
                            (departmentId != null && t.DepartmentID == departmentId) ||
                            (collegeId != null && t.CollegeID == collegeId));

                case RoleNames.Chairperson:
                case RoleNames.Faculty:
                    return a =>
                        (wide && (a.AuthorID == userId ||
                                  a.AnnouncementTags.Any(t => t.DepartmentTag.ShortName == SchoolWide))) ||
                        a.AnnouncementTags.Any(t => tagIds.Contains(t.TagID)) ||
                        a.AnnouncementTargets.Any(t =>
                            (departmentId != null &&
                                (t.DepartmentID == departmentId ||
                                 t.AcademicProgram!.DepartmentID == departmentId)) ||
                            (collegeId != null && t.CollegeID == collegeId));

                case RoleNames.Dean:
                    return a =>
                        (wide && (a.AuthorID == userId ||
                                  a.AnnouncementTags.Any(t => t.DepartmentTag.ShortName == SchoolWide))) ||
                        a.AnnouncementTags.Any(t => tagIds.Contains(t.TagID)) ||
                        a.AnnouncementTargets.Any(t =>
                            collegeId != null &&
                                (t.CollegeID == collegeId ||
                                 t.Department!.CollegeID == collegeId ||
                                 t.AcademicProgram!.Department.CollegeID == collegeId));

                default:
                    // Staff, and any role without a placement: tags only.
                    return a =>
                        (wide && (a.AuthorID == userId ||
                                  a.AnnouncementTags.Any(t => t.DepartmentTag.ShortName == SchoolWide))) ||
                        a.AnnouncementTags.Any(t => tagIds.Contains(t.TagID));
            }
        }
    }
}
