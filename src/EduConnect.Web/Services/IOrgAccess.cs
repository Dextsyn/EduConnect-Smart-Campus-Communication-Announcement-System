using EduConnect.Web.Models;

namespace EduConnect.Web.Services
{
    // Who may join an organization and who may post for one. Checked
    // against the database on every request, so deactivating a student,
    // changing their role or ending their membership takes their posting
    // rights away at once.
    public interface IOrgAccess
    {
        // Active, verified Students — the only accounts that can be members
        // or officers.
        IQueryable<User> EligibleStudents();

        // True when the user is an eligible student holding an active
        // Officer row in this active organization.
        Task<bool> IsOfficerAsync(int userId, int orgId);

        // The organization the user is currently an officer of, if any.
        // A student may be an officer of one organization only.
        Task<Organization?> OfficerOrgAsync(int userId);
    }
}
