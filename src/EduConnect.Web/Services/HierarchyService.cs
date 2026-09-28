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

        // ─── Helpers ───────────────────────────────

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
