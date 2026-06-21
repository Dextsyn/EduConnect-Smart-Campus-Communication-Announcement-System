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

        public HomeController(
            ApplicationDbContext context,
            ILogger<HomeController> logger,
            IFeedRankingService feedRanking)
        {
            _context = context;
            _logger = logger;
            _feedRanking = feedRanking;
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

            if (roleName == "Administrator")
                return RedirectToAction("Index", "Admin");

            if (roleName == "Dean")
                return RedirectToAction("Index", "Dean");

            if (roleName == "Faculty")
                return RedirectToAction("Index", "Faculty");

            if (roleName == "Staff")
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

            if (roleName == "Administrator")
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

            if (roleName == "Faculty" ||
                roleName == "Staff")
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

            // ─── Graph Data ────────────────────
            // Last 6 months labels
            var months = Enumerable.Range(0, 6)
                .Select(i => DateTime.Now.AddMonths(-i))
                .Reverse()
                .ToList();

            model.MonthLabels = months
                .Select(m => m.ToString("MMM yyyy"))
                .ToList();

            model.MonthlyCount = months
                .Select(m => _context.Announcements
                    .Count(a =>
                        a.Status == "Published" &&
                        a.PublishedAt.HasValue &&
                        a.PublishedAt.Value.Month == m.Month &&
                        a.PublishedAt.Value.Year == m.Year))
                .ToList();

            // Announcements by category
            var categoryData = await _context
                .Announcements
                .Where(a => a.Status == "Published")
                .GroupBy(a => a.Category.CategoryName)
                .Select(g => new
                {
                    Category = g.Key,
                    Count = g.Count()
                })
                .ToListAsync();

            model.CategoryLabels = categoryData
                .Select(c => c.Category).ToList();
            model.CategoryCount = categoryData
                .Select(c => c.Count).ToList();

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

            if (roleName == "Student")
            {
                // Personalized feed: 3 sections ranked by behavior
                var userTagIDs = await _context
                    .UserDepartments
                    .Where(ud => ud.UserID == userID)
                    .Select(ud => ud.TagID)
                    .ToListAsync();

                var feed = await _feedRanking.GetPersonalizedFeedAsync(
                    userID, userTagIDs, searchQuery, filterFeedType);

                model.DepartmentAnnouncements = feed.Department;
                model.ForYouAnnouncements = feed.ForYou;
                model.ExploreAnnouncements = feed.Explore;

                return View(model);
            }

            // For faculty/staff — show their own
            if (roleName == "Faculty" ||
                roleName == "Staff")
                query = query.Where(a =>
                    a.AuthorID == userID);

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

            return View(model);


        }

    }
}