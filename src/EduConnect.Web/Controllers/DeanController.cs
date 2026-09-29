using EduConnect.Web.Data;
using EduConnect.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EduConnect.Web.Controllers
{
    public class DeanController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<DeanController> _logger;
        private readonly IAudienceService _audience;

        public DeanController(
            ApplicationDbContext context,
            ILogger<DeanController> logger,
            IAudienceService audience)
        {
            _context = context;
            _logger = logger;
            _audience = audience;
        }

        // ─── Helpers ───────────────────────────
        private bool IsDean() =>
            HttpContext.Session.GetString("RoleName")
                is RoleNames.Dean or RoleNames.Chairperson;

        private int GetUserID() =>
            int.Parse(HttpContext.Session
                .GetString("UserID"));

        // ═══════════════════════════════════════
        //  GET: /Dean
        //  Dean Dashboard
        // ═══════════════════════════════════════
        public async Task<IActionResult> Index()
        {
            if (!IsDean())
                return RedirectToAction(
                    "Login", "Account");

            var userID = GetUserID();

            var viewer = await _audience.GetViewerAsync(userID);
            var addressed = _audience.AddressedTo(viewer);
            var isDean = viewer.RoleName == RoleNames.Dean;
            var collegeID = viewer.CollegeID;
            var departmentID = viewer.DepartmentID;

            // A Dean's scope is the college, a Chairperson's the department.
            var scope = await _context.Users
                .Where(u => u.UserID == userID)
                .Select(u => new
                {
                    CollegeName = u.College == null ? null : u.College.Name,
                    CollegeShort = u.College == null ? null : u.College.ShortName,
                    DepartmentName = u.Department == null || u.Department.IsImplicit ? null : u.Department.Name,
                    DepartmentShort = u.Department == null || u.Department.IsImplicit ? null : u.Department.ShortName
                })
                .FirstOrDefaultAsync();

            // The account was deleted while its session was still alive.
            if (scope == null)
            {
                HttpContext.Session.Clear();
                return RedirectToAction("Login", "Account");
            }

            ViewBag.DepartmentName = (isDean ? scope.CollegeName : scope.DepartmentName ?? scope.CollegeName)
                ?? "No placement";
            ViewBag.DepartmentShort = (isDean ? scope.CollegeShort : scope.DepartmentShort ?? scope.CollegeShort)
                ?? "—";

            // ─── Stat Cards ────────────────────
            ViewBag.TotalPublished = await _context.Announcements
                .Where(a => a.Status == "Published")
                .Where(addressed)
                .CountAsync();

            ViewBag.TotalFaculty = await _context.Users
                .Where(u => u.IsActive &&
                            u.Role.RoleName == RoleNames.Faculty &&
                            (isDean
                                ? collegeID != null && u.CollegeID == collegeID
                                : departmentID != null && u.DepartmentID == departmentID))
                .CountAsync();

            ViewBag.TodayAnnouncements = await _context.Announcements
                .Where(a => a.Status == "Published" &&
                            a.PublishedAt.HasValue &&
                            a.PublishedAt.Value.Date == DateTime.Today)
                .Where(addressed)
                .CountAsync();

            // ─── Chart Data ────────────────────
            var months = Enumerable.Range(0, 6)
                .Select(i => DateTime.Now.AddMonths(-i))
                .Reverse()
                .ToList();

            ViewBag.MonthLabels = months
                .Select(m => m.ToString("MMM yyyy"))
                .ToList();

            ViewBag.MonthlyCount = months
                .Select(m => _context.Announcements
                    .Where(addressed)
                    .Count(a =>
                        a.Status == "Published" &&
                        a.PublishedAt.HasValue &&
                        a.PublishedAt.Value.Month
                            == m.Month &&
                        a.PublishedAt.Value.Year
                            == m.Year))
                .ToList();

            // ─── Recent Published ───────────────
            ViewBag.RecentAnnouncements = await _context
                .Announcements
                .Include(a => a.Author)
                .Include(a => a.Category)
                .Where(a => a.Status == "Published")
                .Where(addressed)
                .OrderByDescending(a => a.PublishedAt)
                .Take(10)
                .ToListAsync();

            return View();
        }

    }
}