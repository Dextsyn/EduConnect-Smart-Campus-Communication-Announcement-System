using EduConnect.Web.Data;
using EduConnect.Web.Models;
using EduConnect.Web.Services;
using EduConnect.Web.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EduConnect.Web.Controllers
{
    public class HomeController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<HomeController> _logger;
        private readonly IFeedRankingService _feedRanking;
        private readonly IAudienceService _audience;

        public HomeController(
            ApplicationDbContext context,
            ILogger<HomeController> logger,
            IFeedRankingService feedRanking,
            IAudienceService audience)
        {
            _context = context;
            _logger = logger;
            _feedRanking = feedRanking;
            _audience = audience;
        }

        public async Task<IActionResult> Index(
            string? searchQuery,
            string? filterCategory,
            string? filterFeedType)
        {
            // Check if logged in
            if (HttpContext.Session.GetString("UserID") == null)
                return RedirectToAction("Login", "Account");

            var userID = int.Parse(HttpContext.Session.GetString("UserID"));
            var roleName = HttpContext.Session.GetString("RoleName");

            if (roleName == RoleNames.Administrator)
                return RedirectToAction("Index", "Admin");

            if (roleName == RoleNames.Dean || roleName == RoleNames.Chairperson)
                return RedirectToAction("Index", "Dean");

            if (roleName == RoleNames.Faculty)
                return RedirectToAction("Index", "Faculty");

            if (roleName == RoleNames.Staff)
                return RedirectToAction("Index", "Staff");

            var model = new DashboardViewModel
            {
                SearchQuery = searchQuery,
                FilterCategory = filterCategory,
                FilterFeedType = filterFeedType
            };

            // ─── Stat Cards ────────────────────
            model.TotalAnnouncements = await _context
                .Announcements
                .Where(a => a.Status == "Published")
                .CountAsync();

            model.TodayAnnouncements = await _context
                .Announcements
                .Where(a => a.Status == "Published" &&
                       a.PublishedAt.HasValue &&
                       a.PublishedAt.Value.Date == DateTime.Today)
                .CountAsync();

            model.UnreadNotifications = await _context
                .Notifications
                .Where(n => n.UserID == userID &&
                            n.IsRead == false)
                .CountAsync();

            model.UpcomingEvents = await _context
                .Events
                .Where(e => e.StartDateTime >= DateTime.Now)
                .CountAsync();

            if (roleName == RoleNames.Administrator)
            {
                model.TotalUsers = await _context
                    .Users
                    .Where(u => u.IsActive)
                    .CountAsync();

                model.PendingFeedback = await _context
                    .Feedbacks
                    .Where(f => !f.IsAcknowledged)
                    .CountAsync();
            }

            if (roleName == RoleNames.Faculty ||
                roleName == RoleNames.Staff)
            {
                model.MyAnnouncements = await _context
                    .Announcements
                    .Where(a => a.AuthorID == userID)
                    .CountAsync();

                model.TotalViews = await _context
                    .Announcements
                    .Where(a => a.AuthorID == userID)
                    .SumAsync(a => a.ViewCount);
            }

            // ─── Table Data ────────────────────
            var query = _context.Announcements
                .Include(a => a.Category)
                .Include(a => a.Author)
                .Include(a => a.AnnouncementTags)
                    .ThenInclude(at => at.DepartmentTag)
                .Where(a => a.Status == "Published")
                .AsQueryable();

            // Apply search
            if (!string.IsNullOrEmpty(searchQuery))
                query = query.Where(a =>
                    a.Title.Contains(searchQuery) ||
                    a.Body.Contains(searchQuery));

            // Apply category filter
            if (!string.IsNullOrEmpty(filterCategory))
                query = query.Where(a =>
                    a.Category.CategoryName == filterCategory);

            // Apply feed filter
            if (!string.IsNullOrEmpty(filterFeedType))
                query = query.Where(a =>
                    a.FeedType == filterFeedType);

            model.UpcomingEventsList = await _context.Events
                .Where(e => e.StartDateTime >= DateTime.Now
                         && e.Status != "Cancelled")
                .OrderBy(e => e.StartDateTime)
                .Take(3)
                .Select(e => new UpcomingEventItem
                {
                    EventID = e.EventID,
                    EventTitle = e.EventTitle,
                    StartDateTime = e.StartDateTime,
                    Location = e.Location ?? "",
                    IsOnline = e.IsOnline,
                    MeetingURL = e.MeetingURL
                })
                .ToListAsync();

            if (roleName == RoleNames.Student)
            {
                // Personalized feed: 3 sections ranked by behavior
                var viewer = await _audience.GetViewerAsync(userID);
                var feed = await _feedRanking.GetPersonalizedFeedAsync(
                    viewer, searchQuery, filterFeedType);

                model.DepartmentAnnouncements = feed.Department;
                model.ForYouAnnouncements = feed.ForYou;
                model.ExploreAnnouncements = feed.Explore;

                return View(model);
            }

            // For faculty/staff — show their own
            if (roleName == RoleNames.Faculty ||
                roleName == RoleNames.Staff)
                query = query.Where(a =>
                    a.AuthorID == userID);

            // Fail closed: every named role returned above, so anything
            // still here (Student Pending, or a role added later) gets the
            // same department scope a Student would, never the full feed.
            var scopedViewer = await _audience.GetViewerAsync(userID);
            query = query.Where(_audience.VisibleTo(scopedViewer));

            model.RecentAnnouncements = await query
                .OrderByDescending(a => a.PublishedAt)
                .Take(10)
                .Select(a => new AnnouncementTableViewModel
                {
                    AnnouncementID = a.AnnouncementID,
                    Title = a.Title,
                    CategoryName = a.Category.CategoryName,
                    CategoryColor = a.Category.ColorHex,
                    FeedType = a.FeedType,
                    IsEmergency = a.IsEmergency,
                    AuthorName = a.Author.FirstName + " "
                                   + a.Author.LastName,
                    Status = a.Status,
                    ViewCount = a.ViewCount,
                    PublishedAt = a.PublishedAt,
                    Tags = a.AnnouncementTags
                        .Select(at => at.DepartmentTag.ShortName)
                        .ToList()
                })
                .ToListAsync();
            await _audience.AddTargetLabelsAsync(model.RecentAnnouncements);

            return View(model);


        }

    }
}