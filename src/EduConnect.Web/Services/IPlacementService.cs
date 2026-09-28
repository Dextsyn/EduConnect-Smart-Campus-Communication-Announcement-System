using EduConnect.Web.Models;

namespace EduConnect.Web.Services
{
    // The only writer of Users.CollegeID / DepartmentID / ProgramID.
    public interface IPlacementService
    {
        // Sets the user's placement for the given role from the most
        // specific level that role uses, deriving the levels above it.
        // Mutates `user` only; the caller saves.
        Task<HierarchyResult> ApplyAsync(User user, string roleName,
            int? collegeId, int? departmentId, int? programId);

        // Temporary bridge until the feed reads placement (Plan 4): points
        // the student's primary UserDepartments tag at their college's
        // legacy tag, or removes it when the college has none. The user
        // must already be saved; stages changes, the caller saves.
        Task SyncFeedTagAsync(User user);
    }
}
