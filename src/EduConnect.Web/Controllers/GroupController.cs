using EduConnect.Web.Data;
using EduConnect.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EduConnect.Web.Controllers
{
    public class GroupController : Controller
    {
        private readonly ApplicationDbContext _context;

        public GroupController(ApplicationDbContext context)
        {
            _context = context;
        }

        private bool IsLoggedIn() =>
            HttpContext.Session.GetString("UserID") != null;

        private int GetUserID() =>
            int.Parse(HttpContext.Session.GetString("UserID")!);

        private bool IsStudent() =>
            HttpContext.Session.GetString("RoleName") == "Student";

        // GET /Group
        public async Task<IActionResult> Index()
        {
            if (!IsLoggedIn()) return RedirectToAction("Login", "Account");

            var groups = await _context.Groups
                .Include(g => g.Creator)
                .Include(g => g.Members)
                .Where(g => g.Status != "Dissolved")
                .OrderByDescending(g => g.CreatedAt)
                .ToListAsync();

            return View(groups);
        }

        // GET /Group/Create
        public IActionResult Create()
        {
            if (!IsLoggedIn()) return RedirectToAction("Login", "Account");
            if (!IsStudent()) return Forbid();
            return View();
        }

        // POST /Group/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(
            string name, string category, string? description, int maxMembers)
        {
            if (!IsLoggedIn()) return RedirectToAction("Login", "Account");
            if (!IsStudent()) return Forbid();

            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(category)
                || maxMembers < 2 || maxMembers > 50)
            {
                TempData["Error"] = "Please fill in all required fields correctly.";
                return View();
            }

            var userId = GetUserID();

            var group = new Group
            {
                CreatorID = userId,
                Name = name.Trim(),
                Category = category,
                Description = description?.Trim(),
                MaxMembers = maxMembers
            };
            _context.Groups.Add(group);
            await _context.SaveChangesAsync();

            // Creator counts as first member
            _context.GroupMembers.Add(new GroupMember
            {
                GroupID = group.GroupID,
                UserID = userId
            });
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Details), new { id = group.GroupID });
        }

        // GET /Group/Details/{id}
        public async Task<IActionResult> Details(int id)
        {
            if (!IsLoggedIn()) return RedirectToAction("Login", "Account");

            var group = await _context.Groups
                .Include(g => g.Creator)
                .Include(g => g.Members).ThenInclude(m => m.User)
                .FirstOrDefaultAsync(g => g.GroupID == id);

            if (group == null) return NotFound();

            var userId = GetUserID();
            var isMember = group.Members.Any(m => m.UserID == userId);

            ViewBag.IsMember = isMember;
            ViewBag.IsCreator = group.CreatorID == userId;
            ViewBag.UserID = userId;

            // Load last 50 messages in chronological order for members only
            ViewBag.Messages = isMember && group.Status != "Dissolved"
                ? (await _context.GroupMessages
                    .Where(m => m.GroupID == id)
                    .Include(m => m.Sender)
                    .OrderByDescending(m => m.SentAt)
                    .Take(50)
                    .ToListAsync())
                    .AsEnumerable()
                    .Reverse()
                    .ToList()
                : null;

            return View(group);
        }

        // POST /Group/Join/{id}
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Join(int id)
        {
            if (!IsLoggedIn()) return RedirectToAction("Login", "Account");
            if (!IsStudent()) return Forbid();

            var group = await _context.Groups
                .Include(g => g.Members)
                .FirstOrDefaultAsync(g => g.GroupID == id);

            if (group == null) return NotFound();

            if (group.Status != "Open")
            {
                TempData["Error"] = "This group is not open for new members.";
                return RedirectToAction(nameof(Details), new { id });
            }

            var userId = GetUserID();
            if (group.Members.Any(m => m.UserID == userId))
            {
                TempData["Error"] = "You are already in this group.";
                return RedirectToAction(nameof(Details), new { id });
            }

            int countAfterJoin = group.Members.Count + 1;
            _context.GroupMembers.Add(new GroupMember { GroupID = id, UserID = userId });
            if (countAfterJoin >= group.MaxMembers)
            {
                group.Status = "Full";
                group.ChatExpiresAt = DateTime.UtcNow.AddDays(7);
            }
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Details), new { id });
        }

        // POST /Group/Leave/{id}
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Leave(int id)
        {
            if (!IsLoggedIn()) return RedirectToAction("Login", "Account");

            var group = await _context.Groups.FindAsync(id);
            if (group == null) return NotFound();

            var userId = GetUserID();
            if (group.CreatorID == userId)
            {
                TempData["Error"] = "Creators cannot leave. Dissolve the group instead.";
                return RedirectToAction(nameof(Details), new { id });
            }

            var membership = await _context.GroupMembers
                .FirstOrDefaultAsync(m => m.GroupID == id && m.UserID == userId);

            if (membership == null)
            {
                TempData["Error"] = "You are not a member of this group.";
                return RedirectToAction(nameof(Details), new { id });
            }

            _context.GroupMembers.Remove(membership);
            if (group.Status == "Full")
            {
                group.Status = "Open";
                group.ChatExpiresAt = null;
            }
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Index));
        }

        // POST /Group/Dissolve/{id}
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Dissolve(int id)
        {
            if (!IsLoggedIn()) return RedirectToAction("Login", "Account");

            var group = await _context.Groups
                .Include(g => g.Messages)
                .FirstOrDefaultAsync(g => g.GroupID == id);

            if (group == null) return NotFound();
            if (group.CreatorID != GetUserID()) return Forbid();
            if (group.Status == "Dissolved") return RedirectToAction(nameof(Index));

            _context.GroupMessages.RemoveRange(group.Messages);
            group.Status = "Dissolved";
            group.ChatExpiresAt = null;
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Index));
        }
    }
}
