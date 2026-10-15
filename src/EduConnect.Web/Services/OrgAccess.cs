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
    }
}
