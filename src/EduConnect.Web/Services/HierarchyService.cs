using EduConnect.Web.Data;
using EduConnect.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace EduConnect.Web.Services
{
    public class HierarchyService : IHierarchyService
    {
        private const int CollegeNameMax = 100;
        private const int NameMax = 150;
        private const int ShortNameMax = 20;

        private readonly ApplicationDbContext _context;

        public HierarchyService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<List<College>> GetTreeAsync(bool includeRetired)
        {
            // Small table (~70 rows): load it whole, then shape in memory.
            var colleges = await _context.Colleges
                .Include(c => c.Departments)
                    .ThenInclude(d => d.Programs)
                .AsNoTracking()
                .ToListAsync();

            if (!includeRetired)
                colleges = colleges.Where(c => c.IsActive).ToList();

            foreach (var college in colleges)
            {
                college.Departments = college.Departments
                    .Where(d => includeRetired || d.IsActive)
                    .OrderBy(d => d.Name)
                    .ToList();

                foreach (var dept in college.Departments)
                    dept.Programs = dept.Programs
                        .Where(p => includeRetired || p.IsActive)
                        .OrderBy(p => p.Name)
                        .ToList();
            }

            return colleges.OrderBy(c => c.Name).ToList();
        }

        public async Task<HierarchyResult> AddCollegeAsync(
            string? name, string? shortName, bool hasDepartments)
        {
            name = Clean(name);
            shortName = Clean(shortName);
            var invalid = ValidateNames(name, CollegeNameMax, shortName);
            if (invalid != null)
                return invalid;

            var lower = name!.ToLower();
            if (await _context.Colleges.AnyAsync(c => c.Name.ToLower() == lower))
                return HierarchyResult.Fail($"A college named \"{name}\" already exists.");

            var college = new College { Name = name, ShortName = shortName };

            // A college without departments still needs one to hang its
            // programs on; the UI never shows it.
            if (!hasDepartments)
                college.Departments.Add(new Department { Name = name, IsImplicit = true });

            _context.Colleges.Add(college);
            await _context.SaveChangesAsync();
            return HierarchyResult.Success;
        }

        public async Task<HierarchyResult> AddDepartmentAsync(
            int collegeId, string? name, string? shortName)
        {
            name = Clean(name);
            shortName = Clean(shortName);
            var invalid = ValidateNames(name, NameMax, shortName);
            if (invalid != null)
                return invalid;

            var college = await _context.Colleges
                .Include(c => c.Departments)
                .FirstOrDefaultAsync(c => c.CollegeID == collegeId);

            if (college == null)
                return HierarchyResult.Fail("College not found.");
            if (!college.IsActive)
                return HierarchyResult.Fail($"{college.Name} is retired. Restore it first.");
            if (college.Departments.Any(d => d.IsImplicit))
                return HierarchyResult.Fail(
                    $"{college.Name} has no departments; add programs to it directly.");

            var lower = name!.ToLower();
            if (college.Departments.Any(d => d.Name.ToLower() == lower))
                return HierarchyResult.Fail(
                    $"{college.Name} already has a department named \"{name}\".");

            college.Departments.Add(new Department { Name = name, ShortName = shortName });
            await _context.SaveChangesAsync();
            return HierarchyResult.Success;
        }

        public async Task<HierarchyResult> AddProgramAsync(
            int departmentId, string? name, string? shortName)
        {
            name = Clean(name);
            shortName = Clean(shortName);
            var invalid = ValidateNames(name, NameMax, shortName);
            if (invalid != null)
                return invalid;

            var dept = await _context.Departments
                .Include(d => d.College)
                .Include(d => d.Programs)
                .FirstOrDefaultAsync(d => d.DepartmentID == departmentId);

            if (dept == null)
                return HierarchyResult.Fail("Department not found.");
            if (!dept.College.IsActive)
                return HierarchyResult.Fail($"{dept.College.Name} is retired. Restore it first.");
            if (!dept.IsActive)
                return HierarchyResult.Fail($"{dept.Name} is retired. Restore it first.");

            var lower = name!.ToLower();
            if (dept.Programs.Any(p => p.Name.ToLower() == lower))
                return HierarchyResult.Fail(
                    $"{DisplayName(dept)} already has a program named \"{name}\".");

            dept.Programs.Add(new AcademicProgram { Name = name, ShortName = shortName });
            await _context.SaveChangesAsync();
            return HierarchyResult.Success;
        }

        public async Task<HierarchyResult> RenameCollegeAsync(int id, string? name, string? shortName)
        {
            name = Clean(name);
            shortName = Clean(shortName);
            var invalid = ValidateNames(name, CollegeNameMax, shortName);
            if (invalid != null)
                return invalid;

            var college = await _context.Colleges
                .Include(c => c.Departments)
                .FirstOrDefaultAsync(c => c.CollegeID == id);
            if (college == null)
                return HierarchyResult.Fail("College not found.");

            var lower = name!.ToLower();
            if (await _context.Colleges.AnyAsync(c => c.CollegeID != id && c.Name.ToLower() == lower))
                return HierarchyResult.Fail($"A college named \"{name}\" already exists.");

            college.Name = name;
            college.ShortName = shortName;
            college.UpdatedAt = DateTime.Now;

            // The implicit department mirrors its college's name.
            foreach (var dept in college.Departments.Where(d => d.IsImplicit))
            {
                dept.Name = name;
                dept.UpdatedAt = DateTime.Now;
            }

            await _context.SaveChangesAsync();
            return HierarchyResult.Success;
        }

        public async Task<HierarchyResult> RenameDepartmentAsync(int id, string? name, string? shortName)
        {
            name = Clean(name);
            shortName = Clean(shortName);
            var invalid = ValidateNames(name, NameMax, shortName);
            if (invalid != null)
                return invalid;

            var dept = await _context.Departments
                .Include(d => d.College)
                    .ThenInclude(c => c.Departments)
                .FirstOrDefaultAsync(d => d.DepartmentID == id);
            if (dept == null)
                return HierarchyResult.Fail("Department not found.");
            if (dept.IsImplicit)
                return HierarchyResult.Fail(
                    $"{dept.College.Name} has no departments; rename the college instead.");

            var lower = name!.ToLower();
            if (dept.College.Departments.Any(d => d.DepartmentID != id && d.Name.ToLower() == lower))
                return HierarchyResult.Fail(
                    $"{dept.College.Name} already has a department named \"{name}\".");

            dept.Name = name;
            dept.ShortName = shortName;
            dept.UpdatedAt = DateTime.Now;
            await _context.SaveChangesAsync();
            return HierarchyResult.Success;
        }

        public async Task<HierarchyResult> RenameProgramAsync(int id, string? name, string? shortName)
        {
            name = Clean(name);
            shortName = Clean(shortName);
            var invalid = ValidateNames(name, NameMax, shortName);
            if (invalid != null)
                return invalid;

            var program = await _context.Programs
                .Include(p => p.Department)
                    .ThenInclude(d => d.College)
                .Include(p => p.Department)
                    .ThenInclude(d => d.Programs)
                .FirstOrDefaultAsync(p => p.ProgramID == id);
            if (program == null)
                return HierarchyResult.Fail("Program not found.");

            var lower = name!.ToLower();
            if (program.Department.Programs.Any(p => p.ProgramID != id && p.Name.ToLower() == lower))
                return HierarchyResult.Fail(
                    $"{DisplayName(program.Department)} already has a program named \"{name}\".");

            program.Name = name;
            program.ShortName = shortName;
            program.UpdatedAt = DateTime.Now;
            await _context.SaveChangesAsync();
            return HierarchyResult.Success;
        }

        public async Task<HierarchyResult> SetCollegeActiveAsync(int id, bool active)
        {
            var college = await _context.Colleges
                .Include(c => c.Departments)
                    .ThenInclude(d => d.Programs)
                .FirstOrDefaultAsync(c => c.CollegeID == id);
            if (college == null)
                return HierarchyResult.Fail("College not found.");

            var implicitDept = college.Departments.FirstOrDefault(d => d.IsImplicit);

            if (!active)
            {
                var placed = await _context.Users.CountAsync(u => u.CollegeID == id);
                if (placed > 0)
                    return PlacedUsersFail(placed, college.Name, "college");

                var activeDepts = college.Departments.Count(d => !d.IsImplicit && d.IsActive);
                if (activeDepts > 0)
                    return HierarchyResult.Fail(
                        $"{college.Name} has {activeDepts} active department(s). Retire them first.");

                var activePrograms = implicitDept?.Programs.Count(p => p.IsActive) ?? 0;
                if (activePrograms > 0)
                    return HierarchyResult.Fail(
                        $"{college.Name} has {activePrograms} active program(s). Retire them first.");
            }

            SetActive(college, active);
            if (implicitDept != null)
                SetActive(implicitDept, active);

            await _context.SaveChangesAsync();
            return HierarchyResult.Success;
        }

        public async Task<HierarchyResult> SetDepartmentActiveAsync(int id, bool active)
        {
            var dept = await _context.Departments
                .Include(d => d.College)
                .Include(d => d.Programs)
                .FirstOrDefaultAsync(d => d.DepartmentID == id);
            if (dept == null)
                return HierarchyResult.Fail("Department not found.");
            if (dept.IsImplicit)
                return HierarchyResult.Fail(
                    $"{dept.College.Name} has no departments; retire or restore the college instead.");

            if (active)
            {
                if (!dept.College.IsActive)
                    return HierarchyResult.Fail($"Restore {dept.College.Name} first.");
            }
            else
            {
                var placed = await _context.Users.CountAsync(u => u.DepartmentID == id);
                if (placed > 0)
                    return PlacedUsersFail(placed, dept.Name, "department");

                var activePrograms = dept.Programs.Count(p => p.IsActive);
                if (activePrograms > 0)
                    return HierarchyResult.Fail(
                        $"{dept.Name} has {activePrograms} active program(s). Retire them first.");
            }

            SetActive(dept, active);
            await _context.SaveChangesAsync();
            return HierarchyResult.Success;
        }

        public async Task<HierarchyResult> SetProgramActiveAsync(int id, bool active)
        {
            var program = await _context.Programs
                .Include(p => p.Department)
                    .ThenInclude(d => d.College)
                .FirstOrDefaultAsync(p => p.ProgramID == id);
            if (program == null)
                return HierarchyResult.Fail("Program not found.");

            if (active)
            {
                if (!program.Department.College.IsActive)
                    return HierarchyResult.Fail($"Restore {program.Department.College.Name} first.");
                if (!program.Department.IsActive)
                    return HierarchyResult.Fail($"Restore {program.Department.Name} first.");
            }
            else
            {
                // Every placed user counts, pending and deactivated ones
                // included: approval would otherwise land a student in a
                // retired program.
                var placed = await _context.Users.CountAsync(u => u.ProgramID == id);
                if (placed > 0)
                    return PlacedUsersFail(placed, program.Name, "program");
            }

            SetActive(program, active);
            await _context.SaveChangesAsync();
            return HierarchyResult.Success;
        }

        // ─── Helpers ───────────────────────────────

        private static HierarchyResult PlacedUsersFail(int count, string name, string level) =>
            HierarchyResult.Fail(
                $"{count} user{(count == 1 ? " is" : "s are")} placed in {name}. " +
                $"Move {(count == 1 ? "them" : "them all")} to another {level} first.");

        private static void SetActive(College college, bool active)
        {
            college.IsActive = active;
            college.RetiredAt = active ? null : DateTime.Now;
            college.UpdatedAt = DateTime.Now;
        }

        private static void SetActive(Department dept, bool active)
        {
            dept.IsActive = active;
            dept.RetiredAt = active ? null : DateTime.Now;
            dept.UpdatedAt = DateTime.Now;
        }

        private static void SetActive(AcademicProgram program, bool active)
        {
            program.IsActive = active;
            program.RetiredAt = active ? null : DateTime.Now;
            program.UpdatedAt = DateTime.Now;
        }

        private static string? Clean(string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        private static HierarchyResult? ValidateNames(string? name, int nameMax, string? shortName)
        {
            if (name == null)
                return HierarchyResult.Fail("Name is required.");
            if (name.Length > nameMax)
                return HierarchyResult.Fail($"Name must be {nameMax} characters or fewer.");
            if (shortName != null && shortName.Length > ShortNameMax)
                return HierarchyResult.Fail($"Short code must be {ShortNameMax} characters or fewer.");
            return null;
        }

        // An implicit department is shown as its college.
        private static string DisplayName(Department dept) =>
            dept.IsImplicit ? dept.College.Name : dept.Name;
    }
}
