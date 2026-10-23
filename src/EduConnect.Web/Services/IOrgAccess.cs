using EduConnect.Web.Models;

namespace EduConnect.Web.Services
{
    // Who may join an organization, who may post for one, and who may read
    // its posts. Checked against the database on every request, so
    // deactivating a student, changing their role or ending their membership
    // takes their rights away at once.
    public interface IOrgAccess
    {
        // Active, verified Students — the only accounts that can be members,
        // officers or followers.
        IQueryable<User> EligibleStudents();

        // True when the user is an eligible student holding an active
        // Officer row in this active organization.
        Task<bool> IsOfficerAsync(int userId, int orgId);

        // The organization the user is currently an officer of, if any.
        // A student may be an officer of one organization only.
        Task<Organization?> OfficerOrgAsync(int userId);

        // An active member row (any role but the retired Adviser) on an
        // active user.
        Task<bool> IsMemberAsync(int userId, int orgId);

        // Posts of an active org are for Administrators, its members and
        // its followers who are still eligible students. Everyone else
        // sees only its About Us.
        Task<bool> CanSeePostsAsync(int userId, string? roleName, int orgId);

        // The active orgs whose posts the user may see.
        IQueryable<int> VisibleOrgIds(int userId, string? roleName);

        // Who a new post notifies: active members on active users plus
        // followers who are still eligible students, each once.
        IQueryable<int> PostAudience(int orgId);
    }
}
