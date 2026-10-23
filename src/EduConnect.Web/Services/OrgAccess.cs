using EduConnect.Web.Data;
using EduConnect.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace EduConnect.Web.Services
{
    public class OrgAccess : IOrgAccess
    {
        private readonly ApplicationDbContext _context;

        public OrgAccess(ApplicationDbContext context)
        {
            _context = context;
        }

        public IQueryable<User> EligibleStudents() =>
            _context.Users.Where(u =>
                u.IsActive &&
                u.Role.RoleName == RoleNames.Student &&
                u.VerificationStatus == VerificationStatuses.Verified);

        public Task<bool> IsOfficerAsync(int userId, int orgId) =>
            _context.OrgMembers.AnyAsync(m =>
                m.OrgID == orgId &&
                m.UserID == userId &&
                m.OrgRole == OrgRoles.Officer &&
                m.IsActive &&
                m.Organization.IsActive &&
                EligibleStudents().Any(u => u.UserID == userId));

        public Task<Organization?> OfficerOrgAsync(int userId) =>
            _context.OrgMembers
                .Where(m => m.UserID == userId &&
                            m.OrgRole == OrgRoles.Officer &&
                            m.IsActive)
                .Select(m => m.Organization)
                .FirstOrDefaultAsync();

        public Task<bool> IsMemberAsync(int userId, int orgId) =>
            ActiveMembers(orgId).AnyAsync(m => m.UserID == userId);

        public Task<bool> CanSeePostsAsync(int userId, string? roleName, int orgId) =>
            VisibleOrgIds(userId, roleName).AnyAsync(id => id == orgId);

        public IQueryable<int> VisibleOrgIds(int userId, string? roleName)
        {
            var orgs = _context.Organizations.Where(o => o.IsActive);
            if (roleName == RoleNames.Administrator)
                return orgs.Select(o => o.OrgID);

            var eligible = EligibleStudents().Any(u => u.UserID == userId);
            return orgs
                .Where(o =>
                    o.Members.Any(m => m.UserID == userId && m.IsActive &&
                                       m.OrgRole != OrgRoles.Adviser && m.User.IsActive) ||
                    (eligible && o.Followers.Any(f => f.UserID == userId)))
                .Select(o => o.OrgID);
        }

        public IQueryable<int> PostAudience(int orgId) =>
            ActiveMembers(orgId).Select(m => m.UserID)
                .Union(_context.OrgFollowers
                    .Where(f => f.OrgID == orgId &&
                                EligibleStudents().Any(u => u.UserID == f.UserID))
                    .Select(f => f.UserID));

        private IQueryable<OrgMember> ActiveMembers(int orgId) =>
            _context.OrgMembers.Where(m =>
                m.OrgID == orgId && m.IsActive &&
                m.OrgRole != OrgRoles.Adviser && m.User.IsActive);
    }
}
