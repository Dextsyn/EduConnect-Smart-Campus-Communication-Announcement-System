using System.Linq.Expressions;
using EduConnect.Web.Data;
using EduConnect.Web.Models;
using EduConnect.Web.ViewModels;
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

        public async Task<HierarchyResult> ValidateTargetsAsync(int authorId, TargetSelection selection,
            TargetSelection? keep = null)
        {
            if (selection.IsEmpty)
                return HierarchyResult.Success;

            var options = await GetTargetOptionsAsync(authorId);
            var college = options.College;
            if (college == null)
                return HierarchyResult.Fail(
                    "You are not placed in a college yet, so you can only post to tags. " +
                    "Ask your administrator to place you.");

            var colleges = options.CanTargetCollege && college != null
                ? new HashSet<int> { college.CollegeID }
                : new HashSet<int>();
            var departments = options.CanTargetDepartments && college != null
                ? college.Departments.Where(d => !d.IsImplicit).Select(d => d.DepartmentID).ToHashSet()
                : new HashSet<int>();
            var programs = college?.Departments.SelectMany(d => d.Programs).Select(p => p.ProgramID).ToHashSet()
                ?? new HashSet<int>();

            if (keep != null)
            {
                colleges.UnionWith(keep.CollegeIDs);
                departments.UnionWith(keep.DepartmentIDs);
                programs.UnionWith(keep.ProgramIDs);
            }

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

        public async Task<List<int>> GetRecipientIdsAsync(int announcementId, int excludeUserId)
        {
            var tags = await _context.AnnouncementTags
                .Where(t => t.AnnouncementID == announcementId)
                .Select(t => new { t.TagID, t.DepartmentTag.ShortName })
                .ToListAsync();

            var active = _context.Users.Where(u => u.IsActive && u.UserID != excludeUserId);

            if (tags.Any(t => t.ShortName == SchoolWide))
                return await active.Select(u => u.UserID).ToListAsync();

            var targets = await _context.AnnouncementTargets
                .Where(t => t.AnnouncementID == announcementId)
                .ToListAsync();

            var tagIds = tags.Select(t => t.TagID).ToList();
            var collegeIds = targets.Where(t => t.CollegeID != null).Select(t => t.CollegeID).ToList();
            var departmentIds = targets.Where(t => t.DepartmentID != null).Select(t => t.DepartmentID).ToList();
            var programIds = targets.Where(t => t.ProgramID != null).Select(t => t.ProgramID).ToList();

            return await active
                .Where(u =>
                    u.UserDepartments.Any(ud => tagIds.Contains(ud.TagID)) ||
                    (u.CollegeID != null && collegeIds.Contains(u.CollegeID)) ||
                    (u.DepartmentID != null && departmentIds.Contains(u.DepartmentID)) ||
                    (u.ProgramID != null && programIds.Contains(u.ProgramID)))
                .Select(u => u.UserID)
                .ToListAsync();
        }

        public async Task AddTargetLabelsAsync(IEnumerable<AnnouncementTableViewModel> rows)
        {
            var list = rows.ToList();
            var ids = list.Select(r => r.AnnouncementID).ToList();
            if (ids.Count == 0)
                return;

            var labels = await _context.AnnouncementTargets
                .Where(t => ids.Contains(t.AnnouncementID))
                .Select(t => new
                {
                    t.AnnouncementID,
                    Label = t.ProgramID != null
                        ? (t.AcademicProgram!.ShortName ?? t.AcademicProgram.Name)
                        : t.DepartmentID != null
                            ? (t.Department!.ShortName ?? t.Department.Name)
                            : (t.College!.ShortName ?? t.College.Name),
                    Retired = t.ProgramID != null ? !t.AcademicProgram!.IsActive
                        : t.DepartmentID != null ? !t.Department!.IsActive
                        : !t.College!.IsActive
                })
                .ToListAsync();

            foreach (var row in list)
                foreach (var label in labels.Where(l => l.AnnouncementID == row.AnnouncementID))
                {
                    var text = label.Retired ? label.Label + RetiredSuffix : label.Label;
                    if (!row.Tags.Contains(text))
                        row.Tags.Add(text);
                }
        }

        public async Task<List<TargetChoice>> GetOutOfScopeTargetsAsync(int authorId, int announcementId)
        {
            var options = await GetTargetOptionsAsync(authorId);
            var college = options.College;
            var colleges = options.CanTargetCollege && college != null
                ? new HashSet<int> { college.CollegeID } : new HashSet<int>();
            var departments = options.CanTargetDepartments && college != null
                ? college.Departments.Where(d => !d.IsImplicit).Select(d => d.DepartmentID).ToHashSet()
                : new HashSet<int>();
            var programs = college?.Departments.SelectMany(d => d.Programs).Select(p => p.ProgramID).ToHashSet()
                ?? new HashSet<int>();

            var targets = await _context.AnnouncementTargets
                .Where(t => t.AnnouncementID == announcementId)
                .Select(t => new
                {
                    t.CollegeID,
                    t.DepartmentID,
                    t.ProgramID,
                    Name = t.ProgramID != null ? t.AcademicProgram!.Name
                        : t.DepartmentID != null ? t.Department!.Name
                        : t.College!.Name
                })
                .ToListAsync();

            var result = new List<TargetChoice>();
            foreach (var t in targets)
            {
                if (t.CollegeID is int c && !colleges.Contains(c))
                    result.Add(new TargetChoice("College", c, t.Name));
                else if (t.DepartmentID is int d && !departments.Contains(d))
                    result.Add(new TargetChoice("Department", d, t.Name));
                else if (t.ProgramID is int p && !programs.Contains(p))
                    result.Add(new TargetChoice("Program", p, t.Name));
            }
            return result;
        }

        public async Task<bool> HasAudienceAsync(int announcementId) =>
            await _context.AnnouncementTags.AnyAsync(t => t.AnnouncementID == announcementId) ||
            await _context.AnnouncementTargets.AnyAsync(t => t.AnnouncementID == announcementId);

        public async Task<List<string>> GetAudienceNamesAsync(int announcementId)
        {
            var tagNames = await _context.AnnouncementTags
                .Where(t => t.AnnouncementID == announcementId &&
                            !_context.Colleges.Any(c => c.LegacyTagID == t.TagID))
                .Select(t => t.DepartmentTag.TagName)
                .ToListAsync();

            return tagNames
                .Concat(await GetTargetNamesAsync(announcementId))
                .Distinct()
                .ToList();
        }

        public async Task<List<string>> GetTargetNamesAsync(int announcementId)
        {
            var targets = await _context.AnnouncementTargets
                .Where(t => t.AnnouncementID == announcementId)
                .Select(t => new
                {
                    Name = t.ProgramID != null ? t.AcademicProgram!.Name
                        : t.DepartmentID != null ? t.Department!.Name
                        : t.College!.Name,
                    Retired = t.ProgramID != null ? !t.AcademicProgram!.IsActive
                        : t.DepartmentID != null ? !t.Department!.IsActive
                        : !t.College!.IsActive
                })
                .ToListAsync();

            return targets.Select(t => t.Retired ? t.Name + RetiredSuffix : t.Name).ToList();
        }

        private const string RetiredSuffix = " (retired)";

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
