using EduConnect.Web.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EduConnect.Web.Controllers
{
    public class DeanController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<DeanController> _logger;

        public DeanController(
            ApplicationDbContext context,
            ILogger<DeanController> logger)
        {
            _context = context;
            _logger = logger;
        }

        // ─── Helpers ───────────────────────────
        private bool IsDean() =>
            HttpContext.Session
                .GetString("RoleName")
                    == "Dean";

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

            // Get dean's department
            var deanDept = await _context
                .UserDepartments
                .Include(ud => ud.DepartmentTag)
                .FirstOrDefaultAsync(ud =>
                    ud.UserID == userID &&
                    ud.IsPrimary);

            ViewBag.DepartmentName =
                deanDept?.DepartmentTag?.TagName
                ?? "No Department";

            ViewBag.DepartmentShort =
                deanDept?.DepartmentTag?.ShortName
                ?? "—";

            // ─── Stat Cards ────────────────────
            // Pending approvals from THIS department
            var deptTagID = deanDept?.TagID;

            // Total published from this department
            ViewBag.TotalPublished = await _context
                .Announcements
                .Where(a =>
                    a.Status == "Published" &&
                    a.AnnouncementTags.Any(at =>
                        at.TagID == deptTagID))
                .CountAsync();

            // Faculty in this department
            ViewBag.TotalFaculty = await _context
                .UserDepartments
                .Where(ud =>
                    ud.TagID == deptTagID &&
                    ud.User.Role.RoleName == "Faculty")
                .CountAsync();

            // Today's announcements
            ViewBag.TodayAnnouncements = await _context
                .Announcements
                .Where(a =>
                    a.Status == "Published" &&
                    a.PublishedAt.HasValue &&
                    a.PublishedAt.Value.Date
                        == DateTime.Today &&
                    a.AnnouncementTags.Any(at =>
                        at.TagID == deptTagID))
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
                    .Count(a =>
                        a.Status == "Published" &&
                        a.PublishedAt.HasValue &&
                        a.PublishedAt.Value.Month
                            == m.Month &&
                        a.PublishedAt.Value.Year
                            == m.Year &&
                        a.AnnouncementTags.Any(at =>
                            at.TagID == deptTagID)))
                .ToList();

            // ─── Recent Published ───────────────
            ViewBag.RecentAnnouncements = await _context
                .Announcements
                .Include(a => a.Author)
                .Include(a => a.Category)
                .Where(a =>
                    a.Status == "Published" &&
                    a.AnnouncementTags.Any(at =>
                        at.TagID == deptTagID))
                .OrderByDescending(a => a.PublishedAt)
                .Take(10)
                .ToListAsync();

            return View();
        }

    }
}