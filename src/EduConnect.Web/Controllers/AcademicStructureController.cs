using EduConnect.Web.Data;
using EduConnect.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EduConnect.Web.Controllers
{
    // Admin management of the College > Department > Program hierarchy.
    // Every rule lives in IHierarchyService; this only relays its result.
    public class AcademicStructureController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IHierarchyService _hierarchy;

        public AcademicStructureController(
            ApplicationDbContext context,
            IHierarchyService hierarchy)
        {
            _context = context;
            _hierarchy = hierarchy;
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
            return Done(await _hierarchy.AddCollegeAsync(name, shortName, hasDepartments),
                $"Added \"{name?.Trim()}\".");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddDepartment(int collegeId, string? name, string? shortName)
        {
            if (!IsAdmin())
                return RedirectToAction("Login", "Account");
            return Done(await _hierarchy.AddDepartmentAsync(collegeId, name, shortName),
                $"Added \"{name?.Trim()}\".", $"college-{collegeId}");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddProgram(int departmentId, int collegeId, string? name, string? shortName)
        {
            if (!IsAdmin())
                return RedirectToAction("Login", "Account");
            return Done(await _hierarchy.AddProgramAsync(departmentId, name, shortName),
                $"Added \"{name?.Trim()}\".", $"college-{collegeId}");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RenameCollege(int id, string? name, string? shortName)
        {
            if (!IsAdmin())
                return RedirectToAction("Login", "Account");
            return Done(await _hierarchy.RenameCollegeAsync(id, name, shortName),
                "College updated.", $"college-{id}");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RenameDepartment(int id, int collegeId, string? name, string? shortName)
        {
            if (!IsAdmin())
                return RedirectToAction("Login", "Account");
            return Done(await _hierarchy.RenameDepartmentAsync(id, name, shortName),
                "Department updated.", $"college-{collegeId}");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RenameProgram(int id, int collegeId, string? name, string? shortName)
        {
            if (!IsAdmin())
                return RedirectToAction("Login", "Account");
            return Done(await _hierarchy.RenameProgramAsync(id, name, shortName),
                "Program updated.", $"college-{collegeId}");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SetCollegeActive(int id, bool active)
        {
            if (!IsAdmin())
                return RedirectToAction("Login", "Account");
            return Done(await _hierarchy.SetCollegeActiveAsync(id, active),
                active ? "College restored." : "College retired.", $"college-{id}");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SetDepartmentActive(int id, int collegeId, bool active)
        {
            if (!IsAdmin())
                return RedirectToAction("Login", "Account");
            return Done(await _hierarchy.SetDepartmentActiveAsync(id, active),
                active ? "Department restored." : "Department retired.", $"college-{collegeId}");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SetProgramActive(int id, int collegeId, bool active)
        {
            if (!IsAdmin())
                return RedirectToAction("Login", "Account");
            return Done(await _hierarchy.SetProgramActiveAsync(id, active),
                active ? "Program restored." : "Program retired.", $"college-{collegeId}");
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
    }
}
