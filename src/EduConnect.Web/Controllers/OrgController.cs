using EduConnect.Web.Data;
using EduConnect.Web.Models;
using EduConnect.Web.Services;
using EduConnect.Web.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace EduConnect.Web.Controllers
{
    public class OrgController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<OrgController> _logger;
        private readonly IWebHostEnvironment _environment;
        private readonly IBlobStorageService _blobStorageService;

        public OrgController(
            ApplicationDbContext context,
            ILogger<OrgController> logger,
            IWebHostEnvironment environment,
            IBlobStorageService blobStorageService)
        {
            _context = context;
            _logger = logger;
            _environment = environment;
            _blobStorageService = blobStorageService;
        }

        // ─── Helpers ───────────────────────────
        private bool IsLoggedIn() =>
            HttpContext.Session.GetString("UserID") != null;

        private int GetUserID() =>
            int.Parse(HttpContext.Session.GetString("UserID")!);

        private bool IsAdmin() =>
            HttpContext.Session.GetString("RoleName") == "Administrator";

        private async Task<bool> IsAdviserOf(int orgId)
        {
            if (HttpContext.Session.GetString("RoleName") != "Faculty")
                return false;
            var userId = GetUserID();
            return await _context.OrgMembers.AnyAsync(m =>
                m.OrgID == orgId &&
                m.UserID == userId &&
                m.OrgRole == "Adviser" &&
                m.IsActive);
        }

        // ─── GET: /Org ─────────────────────────
        public async Task<IActionResult> Index(int? orgId)
        {
            if (!IsLoggedIn())
                return RedirectToAction("Login", "Account");

            var now = DateTime.Now;
            var orgsQuery = _context.Organizations
                .Include(o => o.DepartmentTag)
                .Where(o => o.IsActive);

            if (orgId.HasValue)
                orgsQuery = orgsQuery.Where(o => o.OrgID == orgId.Value);

            var orgs = await orgsQuery.OrderBy(o => o.OrgName).ToListAsync();

            var orgIds = orgs.Select(o => o.OrgID).ToList();
            var allPosts = await _context.OrgAnnouncements
                .Include(a => a.PostedBy)
                .Where(a => orgIds.Contains(a.OrgID) &&
                            (a.ExpiresAt == null || a.ExpiresAt > now))
                .ToListAsync();

            var postsByOrg = allPosts
                .GroupBy(a => a.OrgID)
                .ToDictionary(g => g.Key, g => g
                    .OrderByDescending(a => a.IsPinned)
                    .ThenByDescending(a => a.PostedAt)
                    .ToList());

            var groups = orgs.Select(org => new OrgFeedGroup
            {
                Org = org,
                Announcements = postsByOrg.TryGetValue(org.OrgID, out var p) ? p : new()
            }).ToList();

            var allOrgs = orgId.HasValue
                ? await _context.Organizations
                    .Where(o => o.IsActive)
                    .OrderBy(o => o.OrgName)
                    .ToListAsync()
                : orgs;

            var vm = new OrgFeedViewModel
            {
                Groups = groups,
                FilterOrgID = orgId,
                OrgOptions = allOrgs
                    .Select(o => new SelectListItem(o.OrgName, o.OrgID.ToString()))
                    .ToList()
            };

            return View(vm);
        }

        // ─── GET: /Org/Details/{id} ────────────
        public async Task<IActionResult> Details(int id)
        {
            if (!IsLoggedIn())
                return RedirectToAction("Login", "Account");

            var org = await _context.Organizations
                .Include(o => o.DepartmentTag)
                .FirstOrDefaultAsync(o => o.OrgID == id && o.IsActive);

            if (org == null) return NotFound();

            var now = DateTime.Now;
            var posts = await _context.OrgAnnouncements
                .Include(a => a.PostedBy)
                .Where(a => a.OrgID == id &&
                            (a.ExpiresAt == null || a.ExpiresAt > now))
                .OrderByDescending(a => a.IsPinned)
                .ThenByDescending(a => a.PostedAt)
                .ToListAsync();

            ViewBag.IsAdviser = await IsAdviserOf(id);
            ViewBag.Announcements = posts;
            return View(org);
        }

        // ─── GET: /Org/Post/{orgId} ────────────
        [HttpGet("Org/Post/{orgId:int}")]
        public async Task<IActionResult> Post(int orgId)
        {
            if (!IsLoggedIn())
                return RedirectToAction("Login", "Account");

            if (!await IsAdviserOf(orgId))
                return RedirectToAction("Index");

            var org = await _context.Organizations
                .FirstOrDefaultAsync(o => o.OrgID == orgId && o.IsActive);

            if (org == null) return NotFound();

            return View(new OrgPostViewModel
            {
                OrgID = orgId,
                OrgName = org.OrgName
            });
        }

        // ─── POST: /Org/Post/{orgId} ───────────
        [HttpPost("Org/Post/{orgId:int}")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Post(int orgId, OrgPostViewModel vm)
        {
            if (!IsLoggedIn())
                return RedirectToAction("Login", "Account");

            if (!await IsAdviserOf(orgId))
                return RedirectToAction("Index");

            var org = await _context.Organizations
                .FirstOrDefaultAsync(o => o.OrgID == orgId && o.IsActive);

            if (org == null) return NotFound();

            vm.OrgID = orgId;
            vm.OrgName = org.OrgName;

            if (!ModelState.IsValid)
                return View(vm);

            string? attachmentUrl = null;
            if (vm.Attachment != null && vm.Attachment.Length > 0)
            {
                var allowedExtensions = new[] { ".pdf", ".doc", ".docx", ".xls", ".xlsx",
                    ".ppt", ".pptx", ".txt", ".jpg", ".jpeg", ".png", ".gif", ".zip" };
                var ext = Path.GetExtension(vm.Attachment.FileName).ToLowerInvariant();
                if (!allowedExtensions.Contains(ext))
                {
                    ModelState.AddModelError("Attachment",
                        "File type not allowed. Accepted: PDF, Word, Excel, PowerPoint, images, ZIP.");
                    return View(vm);
                }

                byte[] attachmentBytes;
                using (var memoryStream = new MemoryStream())
                {
                    await vm.Attachment.CopyToAsync(memoryStream);
                    attachmentBytes = memoryStream.ToArray();
                }
                var fileName = $"{Guid.NewGuid()}{ext}";
                attachmentUrl = await _blobStorageService.UploadAsync(
                    attachmentBytes, fileName, "org-attachments", vm.Attachment.ContentType);
            }

            _context.OrgAnnouncements.Add(new OrgAnnouncement
            {
                OrgID = orgId,
                PostedByID = GetUserID(),
                Title = vm.Title,
                Body = vm.Body,
                AttachmentURL = attachmentUrl,
                IsPinned = vm.IsPinned,
                ExpiresAt = vm.ExpiresAt
            });
            await _context.SaveChangesAsync();

            TempData["Success"] = "Post published successfully.";
            return RedirectToAction("Details", new { id = orgId });
        }

        // ─── GET: /Org/Manage ──────────────────
        public async Task<IActionResult> Manage()
        {
            if (!IsAdmin())
                return RedirectToAction("Login", "Account");

            var orgs = await _context.Organizations
                .Include(o => o.DepartmentTag)
                .Include(o => o.Members)
                    .ThenInclude(m => m.User)
                .OrderBy(o => o.OrgName)
                .ToListAsync();

            return View(orgs);
        }

        // ─── GET: /Org/Create ──────────────────
        public async Task<IActionResult> Create()
        {
            if (!IsAdmin())
                return RedirectToAction("Login", "Account");

            return View(await BuildOrgFormViewModel());
        }

        // ─── POST: /Org/Create ─────────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(OrgFormViewModel vm)
        {
            if (!IsAdmin())
                return RedirectToAction("Login", "Account");

            if (!ModelState.IsValid)
            {
                await PopulateOrgFormDropdowns(vm);
                return View(vm);
            }

            var logoError = ValidateLogo(vm.Logo);
            if (logoError != null)
            {
                ModelState.AddModelError("Logo", logoError);
                await PopulateOrgFormDropdowns(vm);
                return View(vm);
            }

            var org = new Organization
            {
                OrgName = vm.OrgName,
                Description = vm.Description,
                LogoURL = await SaveLogoFile(vm.Logo),
                DepartmentTagID = vm.DepartmentTagID,
                CreatedByID = GetUserID()
            };

            _context.Organizations.Add(org);
            await _context.SaveChangesAsync();

            _context.OrgMembers.Add(new OrgMember
            {
                OrgID = org.OrgID,
                UserID = vm.AdviserUserID,
                OrgRole = "Adviser"
            });
            await _context.SaveChangesAsync();

            TempData["Success"] = $"Organization \"{org.OrgName}\" created.";
            return RedirectToAction("Manage");
        }

        // ─── GET: /Org/Edit/{id} ───────────────
        public async Task<IActionResult> Edit(int id)
        {
            if (!IsAdmin())
                return RedirectToAction("Login", "Account");

            var org = await _context.Organizations
                .Include(o => o.Members)
                .FirstOrDefaultAsync(o => o.OrgID == id);

            if (org == null) return NotFound();

            var currentAdviser = org.Members
                .FirstOrDefault(m => m.OrgRole == "Adviser" && m.IsActive);

            var vm = await BuildOrgFormViewModel();
            vm.OrgName = org.OrgName;
            vm.Description = org.Description;
            vm.ExistingLogoURL = org.LogoURL;
            vm.DepartmentTagID = org.DepartmentTagID;
            vm.AdviserUserID = currentAdviser?.UserID ?? 0;

            ViewBag.OrgID = id;
            return View(vm);
        }

        // ─── POST: /Org/Edit/{id} ──────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, OrgFormViewModel vm)
        {
            if (!IsAdmin())
                return RedirectToAction("Login", "Account");

            var org = await _context.Organizations
                .Include(o => o.Members)
                .FirstOrDefaultAsync(o => o.OrgID == id);

            if (org == null) return NotFound();

            if (!ModelState.IsValid)
            {
                await PopulateOrgFormDropdowns(vm);
                vm.ExistingLogoURL = org.LogoURL;
                ViewBag.OrgID = id;
                return View(vm);
            }

            var logoError = ValidateLogo(vm.Logo);
            if (logoError != null)
            {
                ModelState.AddModelError("Logo", logoError);
                await PopulateOrgFormDropdowns(vm);
                vm.ExistingLogoURL = org.LogoURL;
                ViewBag.OrgID = id;
                return View(vm);
            }

            org.OrgName = vm.OrgName;
            org.Description = vm.Description;
            org.DepartmentTagID = vm.DepartmentTagID;
            org.UpdatedAt = DateTime.Now;

            if (vm.Logo != null && vm.Logo.Length > 0)
            {
                var oldLogoURL = org.LogoURL;
                org.LogoURL = await SaveLogoFile(vm.Logo);
                await DeleteLogoBlobAsync(oldLogoURL);
            }

            var currentAdviser = org.Members
                .FirstOrDefault(m => m.OrgRole == "Adviser" && m.IsActive);

            if (currentAdviser == null || currentAdviser.UserID != vm.AdviserUserID)
            {
                if (currentAdviser != null)
                    currentAdviser.IsActive = false;

                var existing = org.Members.FirstOrDefault(m =>
                    m.UserID == vm.AdviserUserID);

                if (existing != null)
                {
                    existing.OrgRole = "Adviser";
                    existing.IsActive = true;
                }
                else
                    _context.OrgMembers.Add(new OrgMember
                    {
                        OrgID = id,
                        UserID = vm.AdviserUserID,
                        OrgRole = "Adviser"
                    });
            }

            await _context.SaveChangesAsync();

            TempData["Success"] = $"Organization \"{org.OrgName}\" updated.";
            return RedirectToAction("Manage");
        }

        // ─── POST: /Org/Deactivate ─────────────
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Deactivate(int id)
        {
            if (!IsAdmin())
                return RedirectToAction("Login", "Account");

            var org = await _context.Organizations.FindAsync(id);
            if (org == null) return NotFound();

            org.IsActive = !org.IsActive;
            org.UpdatedAt = DateTime.Now;
            await _context.SaveChangesAsync();

            TempData["Success"] = org.IsActive
                ? $"\"{org.OrgName}\" reactivated."
                : $"\"{org.OrgName}\" deactivated.";

            return RedirectToAction("Manage");
        }

        // ─── Private Helpers ───────────────────
        private async Task<OrgFormViewModel> BuildOrgFormViewModel()
        {
            var vm = new OrgFormViewModel();
            await PopulateOrgFormDropdowns(vm);
            return vm;
        }

        private async Task PopulateOrgFormDropdowns(OrgFormViewModel vm)
        {
            var faculty = await _context.Users
                .Where(u => u.Role.RoleName == "Faculty" && u.IsActive)
                .OrderBy(u => u.LastName)
                .ThenBy(u => u.FirstName)
                .ToListAsync();

            vm.FacultyOptions = faculty
                .Select(u => new SelectListItem(
                    $"{u.LastName}, {u.FirstName}", u.UserID.ToString()))
                .ToList();

            var depts = await _context.DepartmentTags
                .OrderBy(d => d.TagName)
                .ToListAsync();

            vm.DepartmentOptions = depts
                .Select(d => new SelectListItem(d.TagName, d.TagID.ToString()))
                .ToList();
        }

        private static readonly string[] AllowedLogoExtensions =
            { ".jpg", ".jpeg", ".png", ".webp" };
        private static readonly string[] AllowedLogoContentTypes =
            { "image/jpeg", "image/png", "image/webp" };

        private static string? ValidateLogo(IFormFile? file)
        {
            if (file == null || file.Length == 0) return null;

            var extension = Path.GetExtension(file.FileName ?? string.Empty)
                .ToLowerInvariant();

            if (!AllowedLogoExtensions.Contains(extension) ||
                !AllowedLogoContentTypes.Contains(file.ContentType?.ToLowerInvariant()))
                return "Please upload a valid image file (JPG, PNG, or WEBP) under 5MB.";

            if (file.Length > 5 * 1024 * 1024)
                return "Please upload a valid image file (JPG, PNG, or WEBP) under 5MB.";

            return null;
        }

        private async Task<string?> SaveLogoFile(IFormFile? file)
        {
            if (file == null || file.Length == 0) return null;

            byte[] fileBytes;
            using (var memoryStream = new MemoryStream())
            {
                await file.CopyToAsync(memoryStream);
                fileBytes = memoryStream.ToArray();
            }

            var extension = Path.GetExtension(file.FileName ?? string.Empty)
                .ToLowerInvariant();
            var fileName = $"{Guid.NewGuid()}{extension}";

            return await _blobStorageService.UploadAsync(
                fileBytes, fileName, "org-logos", file.ContentType);
        }

        private async Task DeleteLogoBlobAsync(string? url)
        {
            if (string.IsNullOrEmpty(url)) return;
            if (!url.Contains("/org-logos/", StringComparison.OrdinalIgnoreCase)) return;

            try
            {
                var blobName = url.Substring(url.LastIndexOf('/') + 1);
                await _blobStorageService.DeleteAsync(blobName, "org-logos");
            }
            catch (Exception ex)
            {
                _logger.LogError("Failed to delete org logo blob: {Error}", ex.Message);
            }
        }
    }
}
