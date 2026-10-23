using System.Linq.Expressions;
using EduConnect.Web.Data;
using EduConnect.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace EduConnect.Web.Services
{
    // Who can see an event. An event is for the college it was created
    // in, or only its department when IsDepartmentOnly; nobody outside
    // that audience sees it in any list or can open it. Besides the
    // audience: the organizer, anyone already registered or waitlisted,
    // and the Dean / Chairperson who manages it (EventAccess). Admins and
    // Staff have no placement and so see no events (product decision).
    public static class EventAudience
    {
        // VisibleTo for a signed-in user, loading their placement.
        public static async Task<Expression<Func<Event, bool>>> ForUserAsync(
            ApplicationDbContext db, int userId, string? role)
        {
            var me = await db.Users
                .Where(u => u.UserID == userId)
                .Select(u => new { u.CollegeID, u.DepartmentID })
                .FirstOrDefaultAsync();
            return VisibleTo(userId, role, me?.CollegeID, me?.DepartmentID);
        }

        public static Expression<Func<Event, bool>> VisibleTo(
            int userId, string? role, int? collegeId, int? departmentId)
        {
            var placed = role == RoleNames.Student ||
                         role == RoleNames.Faculty ||
                         role == RoleNames.Chairperson ||
                         role == RoleNames.Dean;
            var college = placed ? collegeId : null;
            var department = placed ? departmentId : null;
            var deanCollege = role == RoleNames.Dean ? collegeId : null;
            var chairDepartment = role == RoleNames.Chairperson ? departmentId : null;

            return e =>
                e.OrganizerID == userId ||
                e.Registrations.Any(r => r.UserID == userId) ||
                e.Waitlist.Any(w => w.UserID == userId) ||
                (deanCollege != null && e.Organizer.CollegeID == deanCollege) ||
                (chairDepartment != null && e.Organizer.DepartmentID == chairDepartment) ||
                (college != null && e.CollegeID == college &&
                 (!e.IsDepartmentOnly || (department != null && e.DepartmentID == department)));
        }
    }
}
