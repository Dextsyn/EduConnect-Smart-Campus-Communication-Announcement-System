using EduConnect.Web.Data;
using EduConnect.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EduConnect.Web.Controllers
{
    // Admin management of the College > Department > Program hierarchy.
    // Every rule lives in IHierarchyService; this only relays its result
    // and records successful changes in the audit log.
    public class AcademicStructureController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IHierarchyService _hierarchy;
        private readonly IAuditService _audit;

        public AcademicStructureController(
            ApplicationDbContext context,
            IHierarchyService hierarchy,
            IAuditService audit)
        {
            _context = context;
            _hierarchy = hierarchy;
            _audit = audit;
        }

        private bool IsAdmin() =>
            HttpContext.Session.GetString("RoleName") == RoleNames.Administrator;

        public async Task<IActionResult> Index()
        {
            if (!IsAdmin())
                return RedirectToAction("Login", "Account");

            ViewBag.CollegeUsers = await _context.Users
                .Where(u => u.CollegeID != null)
                .GroupBy(u => u.CollegeID!.Value)
                .ToDictionaryAsync(g => g.Key, g => g.Count());
            ViewBag.DepartmentUsers = await _context.Users
                .Where(u => u.DepartmentID != null)
                .GroupBy(u => u.DepartmentID!.Value)
                .ToDictionaryAsync(g => g.Key, g => g.Count());
            ViewBag.ProgramUsers = await _context.Users
                .Where(u => u.ProgramID != null)
                .GroupBy(u => u.ProgramID!.Value)
                .ToDictionaryAsync(g => g.Key, g => g.Count());

            return View(await _hierarchy.GetTreeAsync(includeRetired: true));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddCollege(string? name, string? shortName, bool hasDepartments)
        {
            if (!IsAdmin())
                return RedirectToAction("Login", "Account");
            var result = await _hierarchy.AddCollegeAsync(name, shortName, hasDepartments);
            return await Audited(result, "Create", AuditArea.Colleges, null,
                $"Added college {Label(name, shortName)}.",
                $"Added \"{name?.Trim()}\".");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddDepartment(int collegeId, string? name, string? shortName)
        {
            if (!IsAdmin())
                return RedirectToAction("Login", "Account");
            var college = await CollegeLabelAsync(collegeId);
            var result = await _hierarchy.AddDepartmentAsync(collegeId, name, shortName);
            return await Audited(result, "Create", AuditArea.Departments, null,
                $"Added department {Label(name, shortName)} to {college}.",
                $"Added \"{name?.Trim()}\".", $"college-{collegeId}");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddProgram(int departmentId, int collegeId, string? name, string? shortName)
        {
            if (!IsAdmin())
                return RedirectToAction("Login", "Account");
            var department = await DepartmentLabelAsync(departmentId);
            var result = await _hierarchy.AddProgramAsync(departmentId, name, shortName);
            return await Audited(result, "Create", AuditArea.Programs, null,
                $"Added program {Label(name, shortName)} to {department}.",
                $"Added \"{name?.Trim()}\".", $"college-{collegeId}");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RenameCollege(int id, string? name, string? shortName)
        {
            if (!IsAdmin())
                return RedirectToAction("Login", "Account");
            var old = await CollegeLabelAsync(id);
            var result = await _hierarchy.RenameCollegeAsync(id, name, shortName);
            return await Audited(result, "Rename", AuditArea.Colleges, id,
                $"Renamed college {old} to {Label(name, shortName)}.",
                "College updated.", $"college-{id}");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RenameDepartment(int id, int collegeId, string? name, string? shortName)
        {
            if (!IsAdmin())
                return RedirectToAction("Login", "Account");
            var old = await DepartmentLabelAsync(id);
            var result = await _hierarchy.RenameDepartmentAsync(id, name, shortName);
            return await Audited(result, "Rename", AuditArea.Departments, id,
                $"Renamed department {old} to {Label(name, shortName)}.",
                "Department updated.", $"college-{collegeId}");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RenameProgram(int id, int collegeId, string? name, string? shortName)
        {
            if (!IsAdmin())
                return RedirectToAction("Login", "Account");
            var old = await ProgramLabelAsync(id);
            var result = await _hierarchy.RenameProgramAsync(id, name, shortName);
            return await Audited(result, "Rename", AuditArea.Programs, id,
                $"Renamed program {old} to {Label(name, shortName)}.",
                "Program updated.", $"college-{collegeId}");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SetCollegeActive(int id, bool active)
        {
            if (!IsAdmin())
                return RedirectToAction("Login", "Account");
            var result = await _hierarchy.SetCollegeActiveAsync(id, active);
            return await Audited(result, active ? "Restore" : "Retire", AuditArea.Colleges, id,
                $"{(active ? "Restored" : "Retired")} college {await CollegeLabelAsync(id)}.",
                active ? "College restored." : "College retired.", $"college-{id}");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SetDepartmentActive(int id, int collegeId, bool active)
        {
            if (!IsAdmin())
                return RedirectToAction("Login", "Account");
            var result = await _hierarchy.SetDepartmentActiveAsync(id, active);
            return await Audited(result, active ? "Restore" : "Retire", AuditArea.Departments, id,
                $"{(active ? "Restored" : "Retired")} department {await DepartmentLabelAsync(id)}.",
                active ? "Department restored." : "Department retired.", $"college-{collegeId}");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SetProgramActive(int id, int collegeId, bool active)
        {
            if (!IsAdmin())
                return RedirectToAction("Login", "Account");
            var result = await _hierarchy.SetProgramActiveAsync(id, active);
            return await Audited(result, active ? "Restore" : "Retire", AuditArea.Programs, id,
                $"{(active ? "Restored" : "Retired")} program {await ProgramLabelAsync(id)}.",
                active ? "Program restored." : "Program retired.", $"college-{collegeId}");
        }

        // The hierarchy service has already saved the change; the audit row
        // follows in its own save.
        private async Task<IActionResult> Audited(HierarchyResult result, string action, string area,
            int? recordId, string summary, string successMessage, string? anchor = null)
        {
            if (result.Ok)
            {
                _audit.Record(action, area, recordId, summary);
                await _context.SaveChangesAsync();
            }
            return Done(result, successMessage, anchor);
        }

        private IActionResult Done(HierarchyResult result, string successMessage, string? anchor = null)
        {
            if (result.Ok)
                TempData["Success"] = successMessage;
            else
                TempData["Error"] = result.Error;

            var url = Url.Action("Index") + (anchor == null ? "" : "#" + anchor);
            return Redirect(url!);
        }

        // ─── Labels for audit summaries ────────────

        private static string Label(string? name, string? shortName) =>
            string.IsNullOrWhiteSpace(shortName)
                ? $"\"{name?.Trim()}\""
                : $"\"{name?.Trim()}\" ({shortName.Trim()})";

        private async Task<string> CollegeLabelAsync(int id)
        {
            var c = await _context.Colleges.Where(c => c.CollegeID == id)
                .Select(c => new { c.Name, c.ShortName }).FirstOrDefaultAsync();
            return c == null ? $"#{id}" : Label(c.Name, c.ShortName);
        }

        // An implicit department is shown as its college.
        private async Task<string> DepartmentLabelAsync(int id)
        {
            var d = await _context.Departments.Where(d => d.DepartmentID == id)
                .Select(d => new { d.Name, d.ShortName, d.IsImplicit, College = d.College.Name })
                .FirstOrDefaultAsync();
            return d == null ? $"#{id}" : d.IsImplicit ? $"\"{d.College}\"" : Label(d.Name, d.ShortName);
        }

        private async Task<string> ProgramLabelAsync(int id)
        {
            var p = await _context.Programs.Where(p => p.ProgramID == id)
                .Select(p => new { p.Name, p.ShortName }).FirstOrDefaultAsync();
            return p == null ? $"#{id}" : Label(p.Name, p.ShortName);
        }
    }
}
