using EduConnect.Web.Data;
using EduConnect.Web.Models;
using EduConnect.Web.Services;
using EduConnect.Web.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EduConnect.Web.Controllers
{
    // Admin → Moderation Words: edits the list behind Group Finder
    // moderation. Admin-only; never linked anywhere else, since the list
    // holds explicit words. Changes are audited (who edited the list, not
    // who used a word).
    [Route("Admin/ModerationWords")]
    public class ModerationWordsController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IContentModerationService _moderation;
        private readonly IAuditService _audit;

        public ModerationWordsController(ApplicationDbContext context,
            IContentModerationService moderation, IAuditService audit)
        {
            _context = context;
            _moderation = moderation;
            _audit = audit;
        }

        // Logged out → login; signed in as anyone but an admin → 403.
        private IActionResult? Refuse()
        {
            if (HttpContext.Session.GetString("UserID") == null)
                return RedirectToAction("Login", "Account");
            if (HttpContext.Session.GetString("RoleName") != RoleNames.Administrator)
                return Forbid();
            return null;
        }

        // GET /Admin/ModerationWords
        [HttpGet("")]
        public async Task<IActionResult> Index()
        {
            if (Refuse() is { } refused) return refused;

            var words = await _context.ModerationWords.AsNoTracking()
                .OrderBy(w => w.Word)
                .ToListAsync();
            return View(new ModerationWordsViewModel
            {
                Prohibited = words.Where(w => !w.IsAllowed).ToList(),
                Allowed = words.Where(w => w.IsAllowed).ToList()
            });
        }

        // POST /Admin/ModerationWords/Add
        [HttpPost("Add")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Add(string word, string? language, bool isAllowed)
        {
            if (Refuse() is { } refused) return refused;

            var normalized = ModerationMatcher.NormalizeEntry(word ?? "");
            string? error =
                normalized.Length == 0 ? "Enter a word."
                : normalized.Length > 100 ? "Keep it to 100 characters or fewer."
                : !ModerationLanguages.All.Contains(language) ? "Choose English, Tagalog or Taglish."
                : await _context.ModerationWords.AnyAsync(w => w.Word == normalized)
                    ? "That word is already on the list."
                : null;
            if (error != null)
            {
                TempData["Error"] = error;
                return RedirectToAction(nameof(Index));
            }

            var row = new ModerationWord { Word = normalized, Language = language, IsAllowed = isAllowed };
            _context.ModerationWords.Add(row);
            await _context.SaveChangesAsync();

            _audit.Record("Add", AuditArea.Moderation, row.ModerationWordID,
                $"Added \"{normalized}\" to the moderation list",
                newValues: new { Word = normalized, Language = language, Type = TypeName(isAllowed) });
            await _context.SaveChangesAsync();
            _moderation.InvalidateCache();

            TempData["Success"] = $"Added \"{normalized}\".";
            return RedirectToAction(nameof(Index));
        }

        // POST /Admin/ModerationWords/Remove/5
        [HttpPost("Remove/{id:int}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Remove(int id)
        {
            if (Refuse() is { } refused) return refused;

            var row = await _context.ModerationWords.FindAsync(id);
            if (row == null) return NotFound();

            _context.ModerationWords.Remove(row);
            _audit.Record("Remove", AuditArea.Moderation, row.ModerationWordID,
                $"Removed \"{row.Word}\" from the moderation list",
                oldValues: new { row.Word, row.Language, Type = TypeName(row.IsAllowed) });
            await _context.SaveChangesAsync();
            _moderation.InvalidateCache();

            TempData["Success"] = $"Removed \"{row.Word}\".";
            return RedirectToAction(nameof(Index));
        }

        private static string TypeName(bool isAllowed) => isAllowed ? "Always allowed" : "Blocked";
    }
}
