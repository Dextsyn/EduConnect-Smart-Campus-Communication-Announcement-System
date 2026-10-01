using EduConnect.Web.Data;
using EduConnect.Web.Services;
using EduConnect.Web.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EduConnect.Web.Controllers
{
    public class DeanController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<DeanController> _logger;
        private readonly IAudienceService _audience;
        private readonly IApprovalService _approval;

        // How many pending announcements the dashboard lists before
        // pointing to the full review queue.
        private const int PendingShown = 5;

        public DeanController(
            ApplicationDbContext context,
            ILogger<DeanController> logger,
            IAudienceService audience,
            IApprovalService approval)
        {
            _context = context;
            _logger = logger;
            _audience = audience;
            _approval = approval;
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

            // ─── Pending Announcements ─────────
            // The same set as the review queue: what this viewer can approve.
            var reviewable = _approval.ReviewableBy(viewer);
            ViewBag.PendingCount = await reviewable.CountAsync();

            var pending = await reviewable
                .OrderBy(a => a.SubmittedAt)
                .Take(PendingShown)
                .Select(a => new PendingAnnouncementRow
                {
                    AnnouncementID = a.AnnouncementID,
                    Title = a.Title,
                    AuthorName = a.Author.FirstName + " " + a.Author.LastName,
                    CategoryName = a.Category.CategoryName,
                    SubmittedAt = a.SubmittedAt
                })
                .ToListAsync();

            foreach (var row in pending)
                row.Audience = await _audience.GetAudienceNamesAsync(row.AnnouncementID);

            ViewBag.PendingAnnouncements = pending;

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