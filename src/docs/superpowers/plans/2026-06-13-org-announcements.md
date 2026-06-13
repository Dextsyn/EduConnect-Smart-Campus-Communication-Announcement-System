# Organization Announcements Feed — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a dedicated organization announcements feed where Faculty advisers post on behalf of their orgs, and admins create/manage organizations.

**Architecture:** New `OrgController` owns all org routes. The `ApplicationDbContext` DbSets and EF model config already exist — only a migration is needed. ViewModels live in `ViewModel/OrgViewModel.cs`. Five Razor views under `Views/Org/`.

**Tech Stack:** ASP.NET Core 8 MVC, EF Core (SQL Server), BCrypt session auth, Bootstrap 5, Bootstrap Icons.

---

## File Map

| Action | File |
|--------|------|
| **Run** | EF Core migration + DB update |
| **Create** | `EduConnect.Web/ViewModel/OrgViewModel.cs` |
| **Create** | `EduConnect.Web/Controllers/OrgController.cs` |
| **Create** | `EduConnect.Web/Views/Org/Manage.cshtml` |
| **Create** | `EduConnect.Web/Views/Org/Create.cshtml` |
| **Create** | `EduConnect.Web/Views/Org/Edit.cshtml` |
| **Create** | `EduConnect.Web/Views/Org/Index.cshtml` |
| **Create** | `EduConnect.Web/Views/Org/Details.cshtml` |
| **Create** | `EduConnect.Web/Views/Org/Post.cshtml` |
| **Modify** | `EduConnect.Web/Views/Shared/_Layout.cshtml` |
| **Modify** | `EduConnect.Web/Views/Admin/Index.cshtml` |

---

## Task 1: EF Core Migration

**Files:**
- Run: `dotnet ef migrations add AddOrganizationTables --project EduConnect.Web`
- Run: `dotnet ef database update --project EduConnect.Web`

> Note: `ApplicationDbContext` already has all three `DbSet<>` declarations and `OnModelCreating` config for `Organization`, `OrgMember`, and `OrgAnnouncement`. No context changes needed.

- [ ] **Step 1: Add the migration**

```powershell
cd C:\EduConnect\src
dotnet ef migrations add AddOrganizationTables --project EduConnect.Web
```

Expected: new file `EduConnect.Web/Migrations/<timestamp>_AddOrganizationTables.cs` created.

- [ ] **Step 2: Apply to database**

```powershell
dotnet ef database update --project EduConnect.Web
```

Expected: output ends with `Done.` and tables `Organizations`, `OrgMembers`, `OrgAnnouncements` now exist in SQL Server Express (`EduConnectDB`).

- [ ] **Step 3: Commit**

```powershell
git add EduConnect.Web/Migrations/
git commit -m "feat: add EF migration for Organizations, OrgMembers, OrgAnnouncements"
```

---

## Task 2: ViewModels

**Files:**
- Create: `EduConnect.Web/ViewModel/OrgViewModel.cs`

- [ ] **Step 1: Create the file**

```csharp
// EduConnect.Web/ViewModel/OrgViewModel.cs
using EduConnect.Web.Models;
using Microsoft.AspNetCore.Mvc.Rendering;
using System.ComponentModel.DataAnnotations;

namespace EduConnect.Web.ViewModels
{
    public class OrgFeedViewModel
    {
        public List<OrgFeedGroup> Groups { get; set; } = new();
        public int? FilterOrgID { get; set; }
        public List<SelectListItem> OrgOptions { get; set; } = new();
    }

    public class OrgFeedGroup
    {
        public Organization Org { get; set; }
        public List<OrgAnnouncement> Announcements { get; set; } = new();
    }

    public class OrgFormViewModel
    {
        [Required(ErrorMessage = "Organization name is required")]
        [MaxLength(200)]
        public string OrgName { get; set; }

        [MaxLength(500)]
        public string? Description { get; set; }

        public IFormFile? Logo { get; set; }
        public string? ExistingLogoURL { get; set; }

        public int? DepartmentTagID { get; set; }

        [Required(ErrorMessage = "Please select a Faculty adviser")]
        public int AdviserUserID { get; set; }

        public List<SelectListItem> FacultyOptions { get; set; } = new();
        public List<SelectListItem> DepartmentOptions { get; set; } = new();
    }

    public class OrgPostViewModel
    {
        public int OrgID { get; set; }
        public string OrgName { get; set; } = "";

        [Required(ErrorMessage = "Title is required")]
        [MaxLength(300)]
        public string Title { get; set; }

        [Required(ErrorMessage = "Body is required")]
        public string Body { get; set; }

        public IFormFile? Attachment { get; set; }
        public bool IsPinned { get; set; }
        public DateTime? ExpiresAt { get; set; }
    }
}
```

- [ ] **Step 2: Build to confirm no errors**

```powershell
dotnet build EduConnect.Web
```

Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 3: Commit**

```powershell
git add EduConnect.Web/ViewModel/OrgViewModel.cs
git commit -m "feat: add OrgFeedViewModel, OrgFormViewModel, OrgPostViewModel"
```

---

## Task 3: OrgController

**Files:**
- Create: `EduConnect.Web/Controllers/OrgController.cs`

- [ ] **Step 1: Create the controller**

```csharp
// EduConnect.Web/Controllers/OrgController.cs
using EduConnect.Web.Data;
using EduConnect.Web.Models;
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

        public OrgController(
            ApplicationDbContext context,
            ILogger<OrgController> logger,
            IWebHostEnvironment environment)
        {
            _context = context;
            _logger = logger;
            _environment = environment;
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

            var groups = new List<OrgFeedGroup>();
            foreach (var org in orgs)
            {
                var posts = await _context.OrgAnnouncements
                    .Include(a => a.PostedBy)
                    .Where(a => a.OrgID == org.OrgID &&
                                (a.ExpiresAt == null || a.ExpiresAt > now))
                    .OrderByDescending(a => a.IsPinned)
                    .ThenByDescending(a => a.PostedAt)
                    .ToListAsync();

                groups.Add(new OrgFeedGroup { Org = org, Announcements = posts });
            }

            var allOrgs = await _context.Organizations
                .Where(o => o.IsActive)
                .OrderBy(o => o.OrgName)
                .ToListAsync();

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
        [HttpPost]
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
                var uploadsDir = Path.Combine(
                    _environment.WebRootPath, "uploads", "org-attachments");
                Directory.CreateDirectory(uploadsDir);
                var fileName = $"{Guid.NewGuid()}{Path.GetExtension(vm.Attachment.FileName)}";
                using var stream = new FileStream(
                    Path.Combine(uploadsDir, fileName), FileMode.Create);
                await vm.Attachment.CopyToAsync(stream);
                attachmentUrl = $"/uploads/org-attachments/{fileName}";
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

            org.OrgName = vm.OrgName;
            org.Description = vm.Description;
            org.DepartmentTagID = vm.DepartmentTagID;
            org.UpdatedAt = DateTime.Now;

            if (vm.Logo != null && vm.Logo.Length > 0)
                org.LogoURL = await SaveLogoFile(vm.Logo);

            var currentAdviser = org.Members
                .FirstOrDefault(m => m.OrgRole == "Adviser" && m.IsActive);

            if (currentAdviser == null || currentAdviser.UserID != vm.AdviserUserID)
            {
                if (currentAdviser != null)
                    currentAdviser.IsActive = false;

                var existing = org.Members.FirstOrDefault(m =>
                    m.UserID == vm.AdviserUserID && m.OrgRole == "Adviser");

                if (existing != null)
                    existing.IsActive = true;
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

        private async Task<string?> SaveLogoFile(IFormFile? file)
        {
            if (file == null || file.Length == 0) return null;

            var uploadsDir = Path.Combine(
                _environment.WebRootPath, "uploads", "org-logos");
            Directory.CreateDirectory(uploadsDir);
            var fileName = $"{Guid.NewGuid()}{Path.GetExtension(file.FileName)}";
            using var stream = new FileStream(
                Path.Combine(uploadsDir, fileName), FileMode.Create);
            await file.CopyToAsync(stream);
            return $"/uploads/org-logos/{fileName}";
        }
    }
}
```

- [ ] **Step 2: Build to confirm no errors**

```powershell
dotnet build EduConnect.Web
```

Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 3: Commit**

```powershell
git add EduConnect.Web/Controllers/OrgController.cs
git commit -m "feat: add OrgController with feed, post, and admin CRUD actions"
```

---

## Task 4: Admin — Manage View

**Files:**
- Create: `EduConnect.Web/Views/Org/Manage.cshtml`

- [ ] **Step 1: Create Views/Org/ folder and Manage.cshtml**

```html
@* EduConnect.Web/Views/Org/Manage.cshtml *@
@using EduConnect.Web.Models
@model List<Organization>
@{
    ViewData["Title"] = "Manage Organizations";
}

<!-- ─── PAGE HEADER ──────────────────── -->
<div class="d-flex justify-content-between
            align-items-center mb-4 flex-wrap gap-2">
    <div>
        <h4 class="fw-bold mb-1">
            <i class="bi bi-building me-2 text-primary"></i>
            Organizations
        </h4>
        <small class="text-muted">@Model.Count organization(s)</small>
    </div>
    <a href="/Org/Create" class="btn btn-primary btn-sm">
        <i class="bi bi-plus-circle me-1"></i>
        Add Organization
    </a>
</div>

@if (TempData["Success"] != null)
{
    <div class="alert alert-success alert-dismissible mb-4">
        <i class="bi bi-check-circle me-2"></i>
        @TempData["Success"]
        <button type="button" class="btn-close" data-bs-dismiss="alert"></button>
    </div>
}

<div class="card border-0 shadow-sm">
    <div class="card-body p-0">
        <div class="table-responsive">
            <table class="table table-hover mb-0">
                <thead class="table-light">
                    <tr>
                        <th>Organization</th>
                        <th>Adviser</th>
                        <th>Department</th>
                        <th>Status</th>
                        <th class="text-end">Actions</th>
                    </tr>
                </thead>
                <tbody>
                    @foreach (var org in Model)
                    {
                        var adviser = org.Members?
                            .FirstOrDefault(m => m.OrgRole == "Adviser" && m.IsActive);
                        <tr>
                            <td>
                                <div class="d-flex align-items-center gap-2">
                                    @if (!string.IsNullOrEmpty(org.LogoURL))
                                    {
                                        <img src="@org.LogoURL"
                                             class="rounded-circle"
                                             width="32" height="32"
                                             style="object-fit:cover;"
                                             alt="Logo" />
                                    }
                                    else
                                    {
                                        <div class="rounded-circle bg-primary bg-opacity-10
                                                    d-flex align-items-center justify-content-center"
                                             style="width:32px;height:32px;">
                                            <i class="bi bi-building text-primary small"></i>
                                        </div>
                                    }
                                    <span class="fw-semibold">@org.OrgName</span>
                                </div>
                            </td>
                            <td>
                                @if (adviser != null)
                                {
                                    @($"{adviser.User.FirstName} {adviser.User.LastName}")
                                }
                                else
                                {
                                    <span class="text-muted fst-italic">None</span>
                                }
                            </td>
                            <td>
                                @if (org.DepartmentTag != null)
                                {
                                    <span class="badge bg-secondary">
                                        @org.DepartmentTag.ShortName
                                    </span>
                                }
                                else
                                {
                                    <span class="text-muted small">University-wide</span>
                                }
                            </td>
                            <td>
                                @if (org.IsActive)
                                {
                                    <span class="badge bg-success">Active</span>
                                }
                                else
                                {
                                    <span class="badge bg-secondary">Inactive</span>
                                }
                            </td>
                            <td class="text-end">
                                <a href="/Org/Edit/@org.OrgID"
                                   class="btn btn-outline-primary btn-sm me-1">
                                    <i class="bi bi-pencil"></i>
                                </a>
                                <form method="post"
                                      action="/Org/Deactivate/@org.OrgID"
                                      class="d-inline"
                                      onsubmit="return confirm('@(org.IsActive ? "Deactivate" : "Reactivate") this organization?')">
                                    @Html.AntiForgeryToken()
                                    <button type="submit"
                                            class="btn btn-sm @(org.IsActive ? "btn-outline-danger" : "btn-outline-success")">
                                        <i class="bi bi-@(org.IsActive ? "x-circle" : "check-circle")"></i>
                                    </button>
                                </form>
                            </td>
                        </tr>
                    }
                    @if (!Model.Any())
                    {
                        <tr>
                            <td colspan="5" class="text-center text-muted py-4">
                                <i class="bi bi-building fs-2 d-block mb-2"></i>
                                No organizations yet.
                                <a href="/Org/Create">Add the first one.</a>
                            </td>
                        </tr>
                    }
                </tbody>
            </table>
        </div>
    </div>
</div>
```

- [ ] **Step 2: Build**

```powershell
dotnet build EduConnect.Web
```

Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 3: Commit**

```powershell
git add EduConnect.Web/Views/Org/Manage.cshtml
git commit -m "feat: add Org/Manage view — admin org list with deactivate"
```

---

## Task 5: Admin — Create and Edit Views

**Files:**
- Create: `EduConnect.Web/Views/Org/Create.cshtml`
- Create: `EduConnect.Web/Views/Org/Edit.cshtml`

- [ ] **Step 1: Create Create.cshtml**

```html
@* EduConnect.Web/Views/Org/Create.cshtml *@
@model EduConnect.Web.ViewModels.OrgFormViewModel
@{
    ViewData["Title"] = "Add Organization";
}

<div class="d-flex justify-content-between align-items-center mb-4">
    <div>
        <h4 class="fw-bold mb-1">
            <i class="bi bi-building-add me-2 text-primary"></i>
            Add Organization
        </h4>
        <small class="text-muted">Create a new student organization</small>
    </div>
    <a href="/Org/Manage" class="btn btn-outline-secondary btn-sm">
        <i class="bi bi-arrow-left me-1"></i> Back
    </a>
</div>

<div class="card border-0 shadow-sm">
    <div class="card-body p-4">
        <form asp-action="Create" method="post" enctype="multipart/form-data">
            @Html.AntiForgeryToken()

            @if (!ViewData.ModelState.IsValid)
            {
                <div class="alert alert-danger mb-4">
                    <i class="bi bi-exclamation-circle me-2"></i>
                    Please fix the errors below.
                </div>
            }

            <div class="row g-4">

                <div class="col-12 col-md-6">
                    <label asp-for="OrgName" class="form-label fw-semibold">
                        Organization Name <span class="text-danger">*</span>
                    </label>
                    <input asp-for="OrgName" class="form-control" />
                    <span asp-validation-for="OrgName" class="text-danger small"></span>
                </div>

                <div class="col-12 col-md-6">
                    <label asp-for="AdviserUserID" class="form-label fw-semibold">
                        Faculty Adviser <span class="text-danger">*</span>
                    </label>
                    <select asp-for="AdviserUserID"
                            asp-items="Model.FacultyOptions"
                            class="form-select">
                        <option value="">— Select Adviser —</option>
                    </select>
                    <span asp-validation-for="AdviserUserID" class="text-danger small"></span>
                </div>

                <div class="col-12">
                    <label asp-for="Description" class="form-label fw-semibold">
                        Description
                    </label>
                    <textarea asp-for="Description"
                              class="form-control"
                              rows="3"></textarea>
                </div>

                <div class="col-12 col-md-6">
                    <label asp-for="DepartmentTagID" class="form-label fw-semibold">
                        Department <span class="text-muted fw-normal">(optional)</span>
                    </label>
                    <select asp-for="DepartmentTagID"
                            asp-items="Model.DepartmentOptions"
                            class="form-select">
                        <option value="">— University-wide —</option>
                    </select>
                </div>

                <div class="col-12 col-md-6">
                    <label asp-for="Logo" class="form-label fw-semibold">
                        Logo <span class="text-muted fw-normal">(optional, image)</span>
                    </label>
                    <input asp-for="Logo"
                           type="file"
                           accept="image/*"
                           class="form-control" />
                </div>

            </div>

            <div class="d-flex gap-2 mt-4">
                <button type="submit" class="btn btn-primary">
                    <i class="bi bi-check-circle me-1"></i> Create Organization
                </button>
                <a href="/Org/Manage" class="btn btn-outline-secondary">Cancel</a>
            </div>

        </form>
    </div>
</div>

@section Scripts {
    @await Html.PartialAsync("_ValidationScriptsPartial")
}
```

- [ ] **Step 2: Create Edit.cshtml**

```html
@* EduConnect.Web/Views/Org/Edit.cshtml *@
@model EduConnect.Web.ViewModels.OrgFormViewModel
@{
    ViewData["Title"] = "Edit Organization";
    int orgId = (int)ViewBag.OrgID;
}

<div class="d-flex justify-content-between align-items-center mb-4">
    <div>
        <h4 class="fw-bold mb-1">
            <i class="bi bi-pencil-square me-2 text-primary"></i>
            Edit Organization
        </h4>
        <small class="text-muted">Update organization details</small>
    </div>
    <a href="/Org/Manage" class="btn btn-outline-secondary btn-sm">
        <i class="bi bi-arrow-left me-1"></i> Back
    </a>
</div>

<div class="card border-0 shadow-sm">
    <div class="card-body p-4">
        <form asp-action="Edit" asp-route-id="@orgId"
              method="post" enctype="multipart/form-data">
            @Html.AntiForgeryToken()

            @if (!ViewData.ModelState.IsValid)
            {
                <div class="alert alert-danger mb-4">
                    <i class="bi bi-exclamation-circle me-2"></i>
                    Please fix the errors below.
                </div>
            }

            <div class="row g-4">

                <div class="col-12 col-md-6">
                    <label asp-for="OrgName" class="form-label fw-semibold">
                        Organization Name <span class="text-danger">*</span>
                    </label>
                    <input asp-for="OrgName" class="form-control" />
                    <span asp-validation-for="OrgName" class="text-danger small"></span>
                </div>

                <div class="col-12 col-md-6">
                    <label asp-for="AdviserUserID" class="form-label fw-semibold">
                        Faculty Adviser <span class="text-danger">*</span>
                    </label>
                    <select asp-for="AdviserUserID"
                            asp-items="Model.FacultyOptions"
                            class="form-select">
                        <option value="">— Select Adviser —</option>
                    </select>
                    <span asp-validation-for="AdviserUserID" class="text-danger small"></span>
                </div>

                <div class="col-12">
                    <label asp-for="Description" class="form-label fw-semibold">
                        Description
                    </label>
                    <textarea asp-for="Description"
                              class="form-control"
                              rows="3"></textarea>
                </div>

                <div class="col-12 col-md-6">
                    <label asp-for="DepartmentTagID" class="form-label fw-semibold">
                        Department <span class="text-muted fw-normal">(optional)</span>
                    </label>
                    <select asp-for="DepartmentTagID"
                            asp-items="Model.DepartmentOptions"
                            class="form-select">
                        <option value="">— University-wide —</option>
                    </select>
                </div>

                <div class="col-12 col-md-6">
                    <label asp-for="Logo" class="form-label fw-semibold">
                        Logo <span class="text-muted fw-normal">(leave blank to keep current)</span>
                    </label>
                    @if (!string.IsNullOrEmpty(Model.ExistingLogoURL))
                    {
                        <div class="mb-2">
                            <img src="@Model.ExistingLogoURL"
                                 class="rounded"
                                 width="64" height="64"
                                 style="object-fit:cover;" />
                        </div>
                    }
                    <input asp-for="Logo"
                           type="file"
                           accept="image/*"
                           class="form-control" />
                    <input type="hidden" asp-for="ExistingLogoURL" />
                </div>

            </div>

            <div class="d-flex gap-2 mt-4">
                <button type="submit" class="btn btn-primary">
                    <i class="bi bi-check-circle me-1"></i> Save Changes
                </button>
                <a href="/Org/Manage" class="btn btn-outline-secondary">Cancel</a>
            </div>

        </form>
    </div>
</div>

@section Scripts {
    @await Html.PartialAsync("_ValidationScriptsPartial")
}
```

- [ ] **Step 3: Build**

```powershell
dotnet build EduConnect.Web
```

Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 4: Commit**

```powershell
git add EduConnect.Web/Views/Org/Create.cshtml EduConnect.Web/Views/Org/Edit.cshtml
git commit -m "feat: add Org/Create and Org/Edit views for admin"
```

---

## Task 6: Public Feed View

**Files:**
- Create: `EduConnect.Web/Views/Org/Index.cshtml`

- [ ] **Step 1: Create Index.cshtml**

```html
@* EduConnect.Web/Views/Org/Index.cshtml *@
@model EduConnect.Web.ViewModels.OrgFeedViewModel
@{
    ViewData["Title"] = "Organizations";
}

<!-- ─── PAGE HEADER ──────────────────── -->
<div class="d-flex justify-content-between
            align-items-center mb-4 flex-wrap gap-2">
    <div>
        <h4 class="fw-bold mb-1">
            <i class="bi bi-people me-2 text-primary"></i>
            Organizations
        </h4>
        <small class="text-muted">Student organization announcements</small>
    </div>
</div>

<!-- ─── FILTER BAR ───────────────────── -->
<div class="card border-0 shadow-sm mb-4">
    <div class="card-body py-2">
        <form method="get" class="d-flex gap-2 align-items-center flex-wrap">
            <select name="orgId"
                    class="form-select form-select-sm"
                    style="width:220px;"
                    onchange="this.form.submit()">
                <option value="">All Organizations</option>
                @foreach (var opt in Model.OrgOptions)
                {
                    <option value="@opt.Value"
                            @(Model.FilterOrgID?.ToString() == opt.Value ? "selected" : "")>
                        @opt.Text
                    </option>
                }
            </select>
            @if (Model.FilterOrgID.HasValue)
            {
                <a href="/Org" class="btn btn-outline-secondary btn-sm">
                    <i class="bi bi-x"></i> Clear
                </a>
            }
        </form>
    </div>
</div>

@if (!Model.Groups.Any())
{
    <div class="card border-0 shadow-sm">
        <div class="card-body text-center py-5 text-muted">
            <i class="bi bi-building fs-1 d-block mb-3"></i>
            No organizations found.
        </div>
    </div>
}

@foreach (var group in Model.Groups)
{
    <div class="card border-0 shadow-sm mb-4">
        <!-- Org Header -->
        <div class="card-header bg-white border-0 pt-3 pb-2">
            <div class="d-flex align-items-center gap-3">
                @if (!string.IsNullOrEmpty(group.Org.LogoURL))
                {
                    <img src="@group.Org.LogoURL"
                         class="rounded-circle"
                         width="40" height="40"
                         style="object-fit:cover;" />
                }
                else
                {
                    <div class="rounded-circle bg-primary bg-opacity-10
                                d-flex align-items-center justify-content-center"
                         style="width:40px;height:40px;">
                        <i class="bi bi-building text-primary"></i>
                    </div>
                }
                <div>
                    <a href="/Org/Details/@group.Org.OrgID"
                       class="fw-bold text-decoration-none text-dark">
                        @group.Org.OrgName
                    </a>
                    @if (group.Org.DepartmentTag != null)
                    {
                        <span class="badge bg-secondary ms-2 small">
                            @group.Org.DepartmentTag.ShortName
                        </span>
                    }
                    else
                    {
                        <span class="badge bg-info ms-2 small">University-wide</span>
                    }
                </div>
                <a href="/Org/Details/@group.Org.OrgID"
                   class="ms-auto btn btn-outline-primary btn-sm">
                    View All
                </a>
            </div>
        </div>

        <!-- Announcements -->
        @if (!group.Announcements.Any())
        {
            <div class="card-body text-muted small fst-italic">
                No announcements yet.
            </div>
        }
        else
        {
            <ul class="list-group list-group-flush">
                @foreach (var post in group.Announcements.Take(3))
                {
                    <li class="list-group-item px-4 py-3">
                        <div class="d-flex justify-content-between
                                    align-items-start gap-2">
                            <div>
                                @if (post.IsPinned)
                                {
                                    <i class="bi bi-pin-angle-fill text-danger me-1 small"></i>
                                }
                                <span class="fw-semibold">@post.Title</span>
                                <div class="text-muted small mt-1">
                                    @post.Body.Length > 120
                                        ? post.Body.Substring(0, 120) + "…"
                                        : post.Body
                                </div>
                            </div>
                            <div class="text-muted small text-nowrap">
                                @post.PostedAt.ToString("MMM d")
                            </div>
                        </div>
                        <div class="text-muted small mt-1">
                            Posted by @post.PostedBy.FirstName @post.PostedBy.LastName
                        </div>
                    </li>
                }
                @if (group.Announcements.Count > 3)
                {
                    <li class="list-group-item px-4 py-2 text-center">
                        <a href="/Org/Details/@group.Org.OrgID"
                           class="small text-primary text-decoration-none">
                            +@(group.Announcements.Count - 3) more
                        </a>
                    </li>
                }
            </ul>
        }
    </div>
}
```

- [ ] **Step 2: Build**

```powershell
dotnet build EduConnect.Web
```

Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 3: Commit**

```powershell
git add EduConnect.Web/Views/Org/Index.cshtml
git commit -m "feat: add Org/Index feed view grouped by organization"
```

---

## Task 7: Org Details View

**Files:**
- Create: `EduConnect.Web/Views/Org/Details.cshtml`

- [ ] **Step 1: Create Details.cshtml**

```html
@* EduConnect.Web/Views/Org/Details.cshtml *@
@using EduConnect.Web.Models
@model Organization
@{
    ViewData["Title"] = Model.OrgName;
    bool isAdviser = (bool)ViewBag.IsAdviser;
    var posts = ViewBag.Announcements as List<OrgAnnouncement>
        ?? new List<OrgAnnouncement>();
}

<!-- ─── ORG HEADER ────────────────────── -->
<div class="card border-0 shadow-sm mb-4">
    <div class="card-body p-4">
        <div class="d-flex align-items-start gap-4 flex-wrap">
            @if (!string.IsNullOrEmpty(Model.LogoURL))
            {
                <img src="@Model.LogoURL"
                     class="rounded-circle"
                     width="80" height="80"
                     style="object-fit:cover;" />
            }
            else
            {
                <div class="rounded-circle bg-primary bg-opacity-10
                            d-flex align-items-center justify-content-center"
                     style="width:80px;height:80px;">
                    <i class="bi bi-building text-primary fs-2"></i>
                </div>
            }
            <div class="flex-grow-1">
                <h4 class="fw-bold mb-1">@Model.OrgName</h4>
                @if (Model.DepartmentTag != null)
                {
                    <span class="badge bg-secondary mb-2">
                        @Model.DepartmentTag.TagName
                    </span>
                }
                else
                {
                    <span class="badge bg-info mb-2">University-wide</span>
                }
                @if (!string.IsNullOrEmpty(Model.Description))
                {
                    <p class="text-muted mb-0">@Model.Description</p>
                }
            </div>
            <div class="d-flex gap-2">
                @if (isAdviser)
                {
                    <a href="/Org/Post/@Model.OrgID"
                       class="btn btn-primary btn-sm">
                        <i class="bi bi-plus-circle me-1"></i>
                        New Post
                    </a>
                }
                <a href="/Org" class="btn btn-outline-secondary btn-sm">
                    <i class="bi bi-arrow-left me-1"></i> Back
                </a>
            </div>
        </div>
    </div>
</div>

<!-- ─── SUCCESS MESSAGE ──────────────── -->
@if (TempData["Success"] != null)
{
    <div class="alert alert-success alert-dismissible mb-4">
        <i class="bi bi-check-circle me-2"></i>
        @TempData["Success"]
        <button type="button" class="btn-close" data-bs-dismiss="alert"></button>
    </div>
}

<!-- ─── ANNOUNCEMENTS ─────────────────── -->
<h6 class="fw-semibold mb-3 text-muted">
    ANNOUNCEMENTS (@posts.Count)
</h6>

@if (!posts.Any())
{
    <div class="card border-0 shadow-sm">
        <div class="card-body text-center py-5 text-muted">
            <i class="bi bi-megaphone fs-1 d-block mb-3"></i>
            No announcements yet.
            @if (isAdviser)
            {
                <div class="mt-2">
                    <a href="/Org/Post/@Model.OrgID" class="btn btn-primary btn-sm">
                        Post the first announcement
                    </a>
                </div>
            }
        </div>
    </div>
}
else
{
    @foreach (var post in posts)
    {
        <div class="card border-0 shadow-sm mb-3">
            <div class="card-body p-4">
                <div class="d-flex justify-content-between align-items-start gap-2">
                    <div class="flex-grow-1">
                        <div class="d-flex align-items-center gap-2 mb-1">
                            @if (post.IsPinned)
                            {
                                <i class="bi bi-pin-angle-fill text-danger"
                                   title="Pinned"></i>
                            }
                            <h6 class="fw-bold mb-0">@post.Title</h6>
                        </div>
                        <p class="text-muted mb-2" style="white-space:pre-line;">
                            @post.Body
                        </p>
                        @if (!string.IsNullOrEmpty(post.AttachmentURL))
                        {
                            <a href="@post.AttachmentURL"
                               target="_blank"
                               class="btn btn-outline-secondary btn-sm">
                                <i class="bi bi-paperclip me-1"></i> Attachment
                            </a>
                        }
                    </div>
                    <div class="text-muted small text-nowrap">
                        @post.PostedAt.ToString("MMM d, yyyy")
                    </div>
                </div>
                <div class="text-muted small mt-2">
                    <i class="bi bi-person-circle me-1"></i>
                    @post.PostedBy.FirstName @post.PostedBy.LastName
                    @if (post.ExpiresAt.HasValue)
                    {
                        <span class="ms-2">
                            <i class="bi bi-clock me-1"></i>
                            Expires @post.ExpiresAt.Value.ToString("MMM d, yyyy")
                        </span>
                    }
                </div>
            </div>
        </div>
    }
}
```

- [ ] **Step 2: Build**

```powershell
dotnet build EduConnect.Web
```

Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 3: Commit**

```powershell
git add EduConnect.Web/Views/Org/Details.cshtml
git commit -m "feat: add Org/Details view with adviser post button"
```

---

## Task 8: Adviser Post View

**Files:**
- Create: `EduConnect.Web/Views/Org/Post.cshtml`

- [ ] **Step 1: Create Post.cshtml**

```html
@* EduConnect.Web/Views/Org/Post.cshtml *@
@model EduConnect.Web.ViewModels.OrgPostViewModel
@{
    ViewData["Title"] = $"New Post — {Model.OrgName}";
}

<div class="d-flex justify-content-between align-items-center mb-4">
    <div>
        <h4 class="fw-bold mb-1">
            <i class="bi bi-megaphone me-2 text-primary"></i>
            New Post
        </h4>
        <small class="text-muted">@Model.OrgName</small>
    </div>
    <a href="/Org/Details/@Model.OrgID"
       class="btn btn-outline-secondary btn-sm">
        <i class="bi bi-arrow-left me-1"></i> Cancel
    </a>
</div>

<div class="card border-0 shadow-sm">
    <div class="card-body p-4">
        <form asp-action="Post"
              asp-route-orgId="@Model.OrgID"
              method="post"
              enctype="multipart/form-data">
            @Html.AntiForgeryToken()

            @if (!ViewData.ModelState.IsValid)
            {
                <div class="alert alert-danger mb-4">
                    <i class="bi bi-exclamation-circle me-2"></i>
                    Please fix the errors below.
                </div>
            }

            <div class="mb-4">
                <label asp-for="Title" class="form-label fw-semibold">
                    Title <span class="text-danger">*</span>
                </label>
                <input asp-for="Title"
                       class="form-control form-control-lg"
                       placeholder="Announcement title" />
                <span asp-validation-for="Title" class="text-danger small"></span>
            </div>

            <div class="mb-4">
                <label asp-for="Body" class="form-label fw-semibold">
                    Body <span class="text-danger">*</span>
                </label>
                <textarea asp-for="Body"
                          class="form-control"
                          rows="8"
                          placeholder="Write your announcement here…"></textarea>
                <span asp-validation-for="Body" class="text-danger small"></span>
            </div>

            <div class="row g-3 mb-4">
                <div class="col-12 col-md-6">
                    <label asp-for="Attachment" class="form-label fw-semibold">
                        Attachment <span class="text-muted fw-normal">(optional)</span>
                    </label>
                    <input asp-for="Attachment"
                           type="file"
                           class="form-control" />
                </div>
                <div class="col-12 col-md-6">
                    <label asp-for="ExpiresAt" class="form-label fw-semibold">
                        Expires At <span class="text-muted fw-normal">(optional)</span>
                    </label>
                    <input asp-for="ExpiresAt"
                           type="date"
                           class="form-control" />
                </div>
            </div>

            <div class="mb-4">
                <div class="form-check">
                    <input asp-for="IsPinned"
                           type="checkbox"
                           class="form-check-input" />
                    <label asp-for="IsPinned" class="form-check-label fw-semibold">
                        <i class="bi bi-pin-angle-fill text-danger me-1"></i>
                        Pin this announcement
                    </label>
                </div>
            </div>

            <div class="d-flex gap-2">
                <button type="submit" class="btn btn-primary">
                    <i class="bi bi-send me-1"></i> Publish
                </button>
                <a href="/Org/Details/@Model.OrgID"
                   class="btn btn-outline-secondary">
                    Cancel
                </a>
            </div>

        </form>
    </div>
</div>

@section Scripts {
    @await Html.PartialAsync("_ValidationScriptsPartial")
}
```

- [ ] **Step 2: Build**

```powershell
dotnet build EduConnect.Web
```

Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 3: Commit**

```powershell
git add EduConnect.Web/Views/Org/Post.cshtml
git commit -m "feat: add Org/Post view for adviser announcements"
```

---

## Task 9: Navigation Changes

**Files:**
- Modify: `EduConnect.Web/Views/Shared/_Layout.cshtml`
- Modify: `EduConnect.Web/Views/Admin/Index.cshtml`

- [ ] **Step 1: Add Organizations nav link to `_Layout.cshtml`**

In `_Layout.cshtml`, after the Events `<li>` (around line 66), add:

```html
<li class="nav-item">
    <a class="nav-link" href="/Org">
        <i class="bi bi-people"></i> Organizations
    </a>
</li>
```

The existing Events nav-item is:
```html
<li class="nav-item">
    <a class="nav-link" href="/Event">
        <i class="bi bi-calendar-event"></i>
        Events
    </a>
</li>
```

Add the Organizations `<li>` immediately after the closing `</li>` of Events.

- [ ] **Step 2: Add Organizations button to Admin dashboard**

In `EduConnect.Web/Views/Admin/Index.cshtml`, inside the header action buttons `<div class="d-flex gap-2 flex-wrap">` (around line 25), add an Organizations link alongside the existing Pending and Add User buttons:

```html
<a href="/Org/Manage" class="btn btn-info btn-sm text-white">
    <i class="bi bi-building me-1"></i>
    Organizations
</a>
```

- [ ] **Step 3: Build**

```powershell
dotnet build EduConnect.Web
```

Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 4: Commit**

```powershell
git add EduConnect.Web/Views/Shared/_Layout.cshtml
git add EduConnect.Web/Views/Admin/Index.cshtml
git commit -m "feat: add Organizations nav link and admin quick-action button"
```

---

## Task 10: End-to-End Manual Verification

- [ ] **Step 1: Run the app**

```powershell
dotnet run --project EduConnect.Web
```

Open browser at `https://localhost:7135`

- [ ] **Step 2: Verify admin flow**

1. Log in as Administrator
2. Go to Admin Dashboard — confirm "Organizations" button is visible in the header
3. Click "Organizations" → `/Org/Manage` loads with empty table and "Add Organization" button
4. Click "Add Organization" → form loads with Faculty adviser dropdown and optional Department dropdown
5. Fill in Org Name, select a Faculty adviser, click Create
6. Confirm redirected to Manage with success message and org listed

- [ ] **Step 3: Verify edit/deactivate**

1. Click Edit on the org → form pre-populated with existing values
2. Change description, save → success message
3. Click deactivate button → org status changes to Inactive
4. Click reactivate → returns to Active

- [ ] **Step 4: Verify adviser flow**

1. Log in as the Faculty user assigned as adviser
2. Navigate to `/Org` → Organizations feed loads with the org listed
3. Click "View All" → Details page opens with "New Post" button visible
4. Click "New Post" → Post form loads
5. Fill in Title, Body, optionally pin it, click Publish
6. Confirm redirected to Details page with the new post displayed
7. Confirm pinned posts show the pin icon and appear first

- [ ] **Step 5: Verify public feed**

1. Log in as a Student
2. Navigate to `/Org` via the navbar "Organizations" link
3. Confirm the org and its posts are visible
4. Confirm the "New Post" button is NOT visible
5. Use the filter dropdown to filter by org name — confirm only that org's posts show
6. Click "Clear" — all orgs shown again

- [ ] **Step 6: Verify expired posts are hidden**

In SQL Server, manually set `ExpiresAt = DATEADD(day, -1, GETDATE())` on a post.
Refresh `/Org` — confirm that post no longer appears.

- [ ] **Step 7: Final commit**

```powershell
git add .
git commit -m "feat: org announcements feed — complete implementation"
```
