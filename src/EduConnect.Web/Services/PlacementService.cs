using System.Linq.Expressions;
using EduConnect.Web.Data;
using EduConnect.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace EduConnect.Web.Services
{
    public class PlacementService : IPlacementService
    {
        private readonly ApplicationDbContext _context;

        public PlacementService(ApplicationDbContext context)
        {
            _context = context;
        }

        // Users whose role needs a placement they do not have yet.
        // Needs Role loaded or translatable (use inside an EF query).
        public static readonly Expression<Func<User, bool>> NeedsPlacement = u =>
            ((u.Role.RoleName == RoleNames.Student ||
              u.Role.RoleName == RoleNames.StudentPending) && u.ProgramID == null) ||
            ((u.Role.RoleName == RoleNames.Chairperson ||
              u.Role.RoleName == RoleNames.Faculty) && u.DepartmentID == null) ||
            (u.Role.RoleName == RoleNames.Dean && u.CollegeID == null);

        public async Task<HierarchyResult> ApplyAsync(User user, string roleName,
            int? collegeId, int? departmentId, int? programId)
        {
            switch (roleName)
            {
                case RoleNames.Student:
                case RoleNames.StudentPending:
                    return await PlaceStudentAsync(user, programId);

                case RoleNames.Chairperson:
                case RoleNames.Faculty:
                    return await PlaceStaffAsync(user, roleName, collegeId, departmentId);

                case RoleNames.Dean:
                    return await PlaceDeanAsync(user, collegeId);

                default:
                    // Administrators and Staff are never placed.
                    Set(user, null, null, null);
                    return HierarchyResult.Success;
            }
        }

        private async Task<HierarchyResult> PlaceStudentAsync(User user, int? programId)
        {
            if (programId == null)
                return HierarchyResult.Fail("Choose the student's program.");

            var program = await _context.Programs
                .Include(p => p.Department)
                    .ThenInclude(d => d.College)
                .FirstOrDefaultAsync(p => p.ProgramID == programId);

            if (program == null || !program.IsActive ||
                !program.Department.IsActive || !program.Department.College.IsActive)
                return HierarchyResult.Fail("Choose an active program.");

            Set(user, program.Department.CollegeID, program.DepartmentID, program.ProgramID);
            return HierarchyResult.Success;
        }

        private async Task<HierarchyResult> PlaceStaffAsync(User user, string roleName,
            int? collegeId, int? departmentId)
        {
            Department? dept = null;

            if (departmentId != null)
                dept = await _context.Departments
                    .Include(d => d.College)
                    .FirstOrDefaultAsync(d => d.DepartmentID == departmentId);
            else if (collegeId != null)
                // A college without departments: its one implicit department.
                dept = await _context.Departments
                    .Include(d => d.College)
                    .FirstOrDefaultAsync(d => d.CollegeID == collegeId && d.IsImplicit);

            if (dept == null || !dept.IsActive || !dept.College.IsActive)
                return HierarchyResult.Fail("Choose an active department.");

            if (dept.IsImplicit && roleName == RoleNames.Chairperson)
                return HierarchyResult.Fail(
                    $"{dept.College.Name} has no departments, so it has no Chairperson. " +
                    "Make this user its Dean or Faculty instead.");

            Set(user, dept.CollegeID, dept.DepartmentID, null);
            return HierarchyResult.Success;
        }

        private async Task<HierarchyResult> PlaceDeanAsync(User user, int? collegeId)
        {
            if (collegeId == null)
                return HierarchyResult.Fail("Choose the Dean's college.");

            var college = await _context.Colleges.FindAsync(collegeId.Value);
            if (college == null || !college.IsActive)
                return HierarchyResult.Fail("Choose an active college.");

            Set(user, college.CollegeID, null, null);
            return HierarchyResult.Success;
        }

        private static void Set(User user, int? collegeId, int? departmentId, int? programId)
        {
            user.CollegeID = collegeId;
            user.DepartmentID = departmentId;
            user.ProgramID = programId;
        }
    }
}
