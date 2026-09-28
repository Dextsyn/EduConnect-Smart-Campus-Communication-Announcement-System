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
