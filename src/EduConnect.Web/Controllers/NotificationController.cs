using EduConnect.Web.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;

namespace EduConnect.Web.Controllers
{
    public class NotificationController : Controller
    {
        private readonly ApplicationDbContext _context;

        public NotificationController(ApplicationDbContext context)
        {
            _context = context;
        }

        private int? GetUserID()
        {
            var s = HttpContext.Session.GetString("UserID");
            return s == null ? null : int.Parse(s);
        }

        // GET /Notification
        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var userId = GetUserID();
            if (userId == null)
                return RedirectToAction("Login", "Account");

            var notifications = await _context.Notifications
                .Where(n => n.UserID == userId)
                .OrderByDescending(n => n.SentAt)
                .ToListAsync();

            ViewBag.UnreadCount = notifications.Count(n => !n.IsRead);
            return View(notifications);
        }

        // GET /Notification/UnreadCount
        [HttpGet]
        public async Task<IActionResult> UnreadCount()
        {
            var userId = GetUserID();
            if (userId == null) return Json(new { count = 0 });

            var count = await _context.Notifications
                .Where(n => n.UserID == userId && !n.IsRead)
                .CountAsync();

            return Json(new { count });
        }

        // GET /Notification/GetRecent
        [HttpGet]
        public async Task<IActionResult> GetRecent()
        {
            var userId = GetUserID();
            if (userId == null) return Json(Array.Empty<object>());

            var notifications = await _context.Notifications
                .Where(n => n.UserID == userId)
                .OrderByDescending(n => n.SentAt)
                .Take(20)
                .Select(n => new
                {
                    notificationId = n.NotificationID,
                    type = n.Type,
                    message = n.Message,
                    link = n.Link,
                    isRead = n.IsRead,
                    sentAt = n.SentAt
                })
                .ToListAsync();

            return Json(notifications);
        }

        // POST /Notification/MarkRead/{id}
        [HttpPost]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> MarkRead(int id)
        {
            var userId = GetUserID();
            if (userId == null) return Unauthorized();

            var notification = await _context.Notifications
                .FirstOrDefaultAsync(n =>
                    n.NotificationID == id && n.UserID == userId);

            if (notification == null) return NotFound();

            notification.IsRead = true;
            notification.ReadAt = DateTime.Now;
            await _context.SaveChangesAsync();

            return Ok();
        }

        // POST /Notification/MarkAllRead
        [HttpPost]
        [IgnoreAntiforgeryToken]
        public async Task<IActionResult> MarkAllRead()
        {
            var userId = GetUserID();
            if (userId == null) return Unauthorized();

            var unread = await _context.Notifications
                .Where(n => n.UserID == userId && !n.IsRead)
                .ToListAsync();

            var now = DateTime.Now;
            foreach (var n in unread)
            {
                n.IsRead = true;
                n.ReadAt = now;
            }

            await _context.SaveChangesAsync();
            return Ok();
        }
    }
}
