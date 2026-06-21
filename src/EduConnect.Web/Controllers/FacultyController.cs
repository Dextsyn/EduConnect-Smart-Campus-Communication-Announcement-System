using EduConnect.Web.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EduConnect.Web.Controllers
{
    public class FacultyController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<FacultyController> _logger;

        public FacultyController(
            ApplicationDbContext context,
            ILogger<FacultyController> logger)
        {
            _context = context;
            _logger = logger;
        }

        // ─── Helpers ───────────────────────────
        private bool IsFaculty() =>
            HttpContext.Session
                .GetString("RoleName") == "Faculty";

        private int GetUserID() =>
            int.Parse(HttpContext.Session
                .GetString("UserID"));

        // ═══════════════════════════════════════
        //  GET: /Faculty
        //  Faculty Dashboard — Event Overview
        // ═══════════════════════════════════════
        public async Task<IActionResult> Index()
        {
            if (!IsFaculty())
                return RedirectToAction(
                    "Login", "Account");

            var userID = GetUserID();

            // Get faculty's department
            var facultyDept = await _context
                .UserDepartments
                .Include(ud => ud.DepartmentTag)
                .FirstOrDefaultAsync(ud =>
                    ud.UserID == userID &&
                    ud.IsPrimary);

            ViewBag.DepartmentName =
                facultyDept?.DepartmentTag?.TagName
                ?? "No Department";

            ViewBag.DepartmentShort =
                facultyDept?.DepartmentTag?.ShortName
                ?? "—";

            // ─── Stat Cards ────────────────────
            ViewBag.TotalEvents = await _context.Events
                .Where(e => e.OrganizerID == userID)
                .CountAsync();

            ViewBag.UpcomingEvents = await _context.Events
                .Where(e =>
                    e.OrganizerID == userID &&
                    e.StartDateTime > DateTime.Now &&
                    e.Status != "Cancelled")
                .CountAsync();

            ViewBag.CompletedEvents = await _context.Events
                .Where(e =>
                    e.OrganizerID == userID &&
                    e.EndDateTime < DateTime.Now)
                .CountAsync();

            ViewBag.TotalRegistrations = await _context
                .EventRegistrations
                .Where(r =>
                    r.Event.OrganizerID == userID &&
                    r.Status != "Cancelled")
                .CountAsync();

            // ─── Chart: events created per month ─
            var months = Enumerable.Range(0, 6)
                .Select(i => DateTime.Now.AddMonths(-i))
                .Reverse()
                .ToList();

            ViewBag.MonthLabels = months
                .Select(m => m.ToString("MMM yyyy"))
                .ToList();

            ViewBag.MonthlyCount = months
                .Select(m => _context.Events
                    .Count(e =>
                        e.OrganizerID == userID &&
                        e.CreatedAt.Month == m.Month &&
                        e.CreatedAt.Year == m.Year))
                .ToList();

            // ─── Recent Events ──────────────────
            ViewBag.MyEventList = await _context.Events
                .Include(e => e.Registrations)
                .Where(e => e.OrganizerID == userID)
                .OrderByDescending(e => e.CreatedAt)
                .Take(10)
                .ToListAsync();

            return View();
        }
    }
}
