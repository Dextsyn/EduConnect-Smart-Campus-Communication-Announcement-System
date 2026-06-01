# Campus Safety Reporting — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let any logged-in user photograph and report a broken campus facility, then notify all active Staff users via in-app notification and email.

**Architecture:** Two controllers — `SafetyReportController` for submission (all logged-in users) and `StaffController` for management (Staff and Administrator only). The existing `IncidentReport` model is reused with `IncidentType` storing the building code. `_Layout.cshtml` gains nav links for both flows.

**Tech Stack:** ASP.NET Core 8 MVC, Entity Framework Core (SQL Server), Bootstrap 5 + Bootstrap Icons, `INotificationService` (SignalR-backed), `IEmailService` (MailKit).

---

## File Map

| Action | Path |
|---|---|
| Create | `EduConnect.Web/ViewModel/SafetyReportViewModel.cs` |
| Create | `EduConnect.Web/Controllers/SafetyReportController.cs` |
| Create | `EduConnect.Web/Controllers/StaffController.cs` |
| Create | `EduConnect.Web/Views/SafetyReport/Submit.cshtml` |
| Create | `EduConnect.Web/Views/SafetyReport/Confirmation.cshtml` |
| Create | `EduConnect.Web/Views/Staff/Index.cshtml` |
| Create | `EduConnect.Web/Views/Staff/ReportDetails.cshtml` |
| Modify | `EduConnect.Web/Views/Shared/_Layout.cshtml` |

No model or migration changes needed — `IncidentReport` and `IncidentReports` DbSet are already in place.

---

## Task 1: ViewModels

**Files:**
- Create: `EduConnect.Web/ViewModel/SafetyReportViewModel.cs`

- [ ] **Step 1: Create the file**

```csharp
using System.ComponentModel.DataAnnotations;

namespace EduConnect.Web.ViewModels
{
    public class SafetyReportViewModel
    {
        [Required(ErrorMessage = "Please select a building.")]
        public string Building { get; set; }

        [MaxLength(255)]
        public string? SpecificLocation { get; set; }

        [Required(ErrorMessage = "Please describe the issue.")]
        public string Description { get; set; }

        public IFormFile? Photo { get; set; }

        public bool IsAnonymous { get; set; }
    }

    public class SafetyReportFilterViewModel
    {
        public string? Building { get; set; }
        public string? Status { get; set; }
    }
}
```

- [ ] **Step 2: Build to verify**

Run from `C:\EduConnect\src`:
```
dotnet build EduConnect.Web
```
Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 3: Commit**

```
git add EduConnect.Web/ViewModel/SafetyReportViewModel.cs
git commit -m "feat: add SafetyReportViewModel and SafetyReportFilterViewModel"
```

---

## Task 2: SafetyReportController

**Files:**
- Create: `EduConnect.Web/Controllers/SafetyReportController.cs`

- [ ] **Step 1: Create the file**

```csharp
using EduConnect.Web.Data;
using EduConnect.Web.Models;
using EduConnect.Web.Services;
using EduConnect.Web.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EduConnect.Web.Controllers
{
    public class SafetyReportController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IWebHostEnvironment _environment;
        private readonly INotificationService _notificationService;
        private readonly IEmailService _emailService;
        private readonly ILogger<SafetyReportController> _logger;

        public SafetyReportController(
            ApplicationDbContext context,
            IWebHostEnvironment environment,
            INotificationService notificationService,
            IEmailService emailService,
            ILogger<SafetyReportController> logger)
        {
            _context = context;
            _environment = environment;
            _notificationService = notificationService;
            _emailService = emailService;
            _logger = logger;
        }

        private bool IsLoggedIn() =>
            HttpContext.Session.GetString("UserID") != null;

        private int GetUserID() =>
            int.Parse(HttpContext.Session.GetString("UserID")!);

        private string GetBaseUrl() =>
            $"{Request.Scheme}://{Request.Host}";

        // GET /SafetyReport
        public IActionResult Index()
        {
            if (!IsLoggedIn())
                return RedirectToAction("Login", "Account");
            return RedirectToAction("Submit");
        }

        // GET /SafetyReport/Submit
        public IActionResult Submit()
        {
            if (!IsLoggedIn())
                return RedirectToAction("Login", "Account");
            return View();
        }

        // POST /SafetyReport/Submit
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Submit(SafetyReportViewModel model)
        {
            if (!IsLoggedIn())
                return RedirectToAction("Login", "Account");

            if (!ModelState.IsValid)
                return View(model);

            string? photoURL = null;
            if (model.Photo != null && model.Photo.Length > 0)
            {
                var allowedExtensions = new[] { ".jpg", ".jpeg", ".png" };
                var extension = Path.GetExtension(
                    model.Photo.FileName).ToLowerInvariant();

                if (!allowedExtensions.Contains(extension) ||
                    model.Photo.Length > 5 * 1024 * 1024)
                {
                    ModelState.AddModelError("Photo",
                        "Photo must be a JPG or PNG under 5 MB.");
                    return View(model);
                }

                var uploadFolder = Path.Combine(
                    _environment.WebRootPath,
                    "uploads", "safety-reports");
                Directory.CreateDirectory(uploadFolder);

                var fileName = Guid.NewGuid().ToString() + extension;
                var filePath = Path.Combine(uploadFolder, fileName);

                using var stream = new FileStream(filePath, FileMode.Create);
                await model.Photo.CopyToAsync(stream);

                photoURL = "/uploads/safety-reports/" + fileName;
            }

            var report = new IncidentReport
            {
                ReportedByID = GetUserID(),
                IncidentType = model.Building,
                Description = model.Description,
                Location = model.SpecificLocation,
                PhotoURL = photoURL,
                IsAnonymous = model.IsAnonymous,
                Status = "Pending",
                ReportedAt = DateTime.Now
            };

            _context.IncidentReports.Add(report);
            await _context.SaveChangesAsync();

            await DispatchNotificationsAsync(report);

            return RedirectToAction("Confirmation", new { id = report.ReportID });
        }

        // GET /SafetyReport/Confirmation/{id}
        public async Task<IActionResult> Confirmation(int id)
        {
            if (!IsLoggedIn())
                return RedirectToAction("Login", "Account");

            var report = await _context.IncidentReports
                .FirstOrDefaultAsync(r => r.ReportID == id);

            if (report == null) return NotFound();

            return View(report);
        }

        private async Task DispatchNotificationsAsync(IncidentReport report)
        {
            var baseUrl = GetBaseUrl();
            try
            {
                var staffUsers = await _context.Users
                    .Include(u => u.Role)
                    .Where(u => u.Role.RoleName == "Staff"
                                && u.IsActive)
                    .ToListAsync();

                if (!staffUsers.Any()) return;

                var locationPart = string.IsNullOrWhiteSpace(report.Location)
                    ? "No specific location"
                    : report.Location;

                var message =
                    $"New safety report submitted — " +
                    $"{report.IncidentType}: {locationPart}";
                var link = $"/Staff/ReportDetails/{report.ReportID}";

                var staffIds = staffUsers
                    .Select(u => u.UserID).ToList();

                await _notificationService.SendToManyAsync(
                    staffIds, "SafetyReport", message, link);

                var reporterLine = report.IsAnonymous
                    ? "<p><em>Submitted anonymously.</em></p>"
                    : "";

                var emailBody = $@"
<h2 style='color:#0d6efd'>New Campus Safety Report</h2>
<table style='border-collapse:collapse;width:100%'>
  <tr><td style='padding:6px;font-weight:bold'>Building</td>
      <td style='padding:6px'>{report.IncidentType}</td></tr>
  <tr><td style='padding:6px;font-weight:bold'>Specific Location</td>
      <td style='padding:6px'>{locationPart}</td></tr>
  <tr><td style='padding:6px;font-weight:bold'>Description</td>
      <td style='padding:6px'>{report.Description}</td></tr>
  <tr><td style='padding:6px;font-weight:bold'>Submitted At</td>
      <td style='padding:6px'>{report.ReportedAt:yyyy-MM-dd HH:mm}</td></tr>
</table>
{reporterLine}
<p style='margin-top:16px'>
  <a href='{baseUrl}{link}'
     style='background:#0d6efd;color:#fff;padding:8px 16px;
            border-radius:4px;text-decoration:none'>
    View Report in EduConnect
  </a>
</p>";

                foreach (var staff in staffUsers)
                {
                    try
                    {
                        await _emailService.SendEmailAsync(
                            staff.Email,
                            $"{staff.FirstName} {staff.LastName}",
                            $"New Safety Report — {report.IncidentType}",
                            emailBody);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex,
                            "Failed to email staff {UserID} for report {ReportID}",
                            staff.UserID, report.ReportID);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Notification dispatch failed for report {ReportID}",
                    report.ReportID);
            }
        }
    }
}
```

- [ ] **Step 2: Build to verify**

```
dotnet build EduConnect.Web
```
Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 3: Commit**

```
git add EduConnect.Web/Controllers/SafetyReportController.cs
git commit -m "feat: add SafetyReportController with submit and notification dispatch"
```

---

## Task 3: Submit view

**Files:**
- Create: `EduConnect.Web/Views/SafetyReport/Submit.cshtml`

- [ ] **Step 1: Create the Views/SafetyReport directory and file**

```cshtml
@model EduConnect.Web.ViewModels.SafetyReportViewModel
@{
    ViewData["Title"] = "Report a Safety Issue";
}

<div class="d-flex justify-content-between align-items-center mb-4">
    <div>
        <h4 class="fw-bold mb-1">
            <i class="bi bi-shield-exclamation me-2 text-warning"></i>
            Report a Safety Issue
        </h4>
        <small class="text-muted">
            Report a broken or damaged facility on campus
        </small>
    </div>
    <a href="/" class="btn btn-outline-secondary btn-sm">
        <i class="bi bi-arrow-left me-2"></i>Back
    </a>
</div>

<div class="row justify-content-center">
    <div class="col-12 col-lg-8">
        <div class="card border-0 shadow-sm">
            <div class="card-body p-4">

                @if (!ViewData.ModelState.IsValid)
                {
                    <div class="alert alert-danger mb-4">
                        <i class="bi bi-exclamation-circle me-2"></i>
                        Please fix the errors below.
                    </div>
                }

                <form asp-action="Submit"
                      method="post"
                      enctype="multipart/form-data">
                    @Html.AntiForgeryToken()

                    <!-- Building -->
                    <div class="mb-4">
                        <label asp-for="Building"
                               class="form-label fw-semibold">
                            Building <span class="text-danger">*</span>
                        </label>
                        <select asp-for="Building"
                                class="form-select form-select-lg">
                            <option value="">— Select building —</option>
                            <option value="SV">SV</option>
                            <option value="ST">ST</option>
                            <option value="OZ">OZ</option>
                            <option value="CS">CS</option>
                            <option value="CT">CT</option>
                            <option value="Other">Other (outdoor / garden)</option>
                        </select>
                        <span asp-validation-for="Building"
                              class="text-danger small"></span>
                    </div>

                    <!-- Specific Location -->
                    <div class="mb-4">
                        <label asp-for="SpecificLocation"
                               class="form-label fw-semibold">
                            Specific Location
                        </label>
                        <input asp-for="SpecificLocation"
                               class="form-control"
                               placeholder="e.g. Room 201, 2nd floor hallway, near the entrance" />
                        <div class="form-text">
                            Optional but helps staff find the issue faster.
                        </div>
                    </div>

                    <!-- Description -->
                    <div class="mb-4">
                        <label asp-for="Description"
                               class="form-label fw-semibold">
                            Description <span class="text-danger">*</span>
                        </label>
                        <textarea asp-for="Description"
                                  class="form-control"
                                  rows="4"
                                  placeholder="Describe what is broken or damaged..."></textarea>
                        <span asp-validation-for="Description"
                              class="text-danger small"></span>
                    </div>

                    <!-- Photo -->
                    <div class="mb-4">
                        <label asp-for="Photo"
                               class="form-label fw-semibold">
                            Photo
                        </label>
                        <input asp-for="Photo"
                               type="file"
                               accept=".jpg,.jpeg,.png"
                               class="form-control" />
                        <div class="form-text">
                            Optional. JPG or PNG, max 5 MB.
                        </div>
                        <span asp-validation-for="Photo"
                              class="text-danger small"></span>
                    </div>

                    <!-- Photo preview -->
                    <div id="photo-preview-wrapper" class="mb-4 d-none">
                        <img id="photo-preview"
                             src="#"
                             alt="Preview"
                             class="img-thumbnail"
                             style="max-height:200px" />
                    </div>

                    <!-- Anonymous -->
                    <div class="mb-4 p-3 bg-light rounded">
                        <div class="form-check">
                            <input asp-for="IsAnonymous"
                                   class="form-check-input"
                                   type="checkbox" />
                            <label asp-for="IsAnonymous"
                                   class="form-check-label fw-semibold">
                                Submit anonymously
                            </label>
                        </div>
                        <div class="form-text mt-1">
                            Your name will not be shown to staff if this is checked.
                        </div>
                    </div>

                    <div class="d-grid">
                        <button type="submit"
                                class="btn btn-warning btn-lg fw-semibold">
                            <i class="bi bi-send me-2"></i>Submit Report
                        </button>
                    </div>

                </form>

            </div>
        </div>
    </div>
</div>

@section Scripts {
    @await Html.PartialAsync("_ValidationScriptsPartial")
    <script>
        document.querySelector('input[type="file"]')
            .addEventListener('change', function (e) {
                const file = e.target.files[0];
                if (!file) return;
                const wrapper = document.getElementById('photo-preview-wrapper');
                const img = document.getElementById('photo-preview');
                img.src = URL.createObjectURL(file);
                wrapper.classList.remove('d-none');
            });
    </script>
}
```

- [ ] **Step 2: Build to verify**

```
dotnet build EduConnect.Web
```
Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 3: Commit**

```
git add EduConnect.Web/Views/SafetyReport/Submit.cshtml
git commit -m "feat: add SafetyReport/Submit view with photo preview"
```

---

## Task 4: Confirmation view

**Files:**
- Create: `EduConnect.Web/Views/SafetyReport/Confirmation.cshtml`

- [ ] **Step 1: Create the file**

```cshtml
@model EduConnect.Web.Models.IncidentReport
@{
    ViewData["Title"] = "Report Submitted";
}

<div class="row justify-content-center mt-4">
    <div class="col-12 col-lg-6 text-center">
        <div class="card border-0 shadow-sm">
            <div class="card-body p-5">
                <div class="mb-4">
                    <i class="bi bi-check-circle-fill text-success"
                       style="font-size:4rem"></i>
                </div>
                <h4 class="fw-bold mb-2">Report Submitted</h4>
                <p class="text-muted mb-4">
                    Thank you. Your safety report has been received and
                    campus staff have been notified.
                </p>
                <div class="p-3 bg-light rounded mb-4 text-start">
                    <div class="d-flex justify-content-between mb-1">
                        <span class="text-muted small">Report ID</span>
                        <span class="fw-semibold small">#@Model.ReportID</span>
                    </div>
                    <div class="d-flex justify-content-between mb-1">
                        <span class="text-muted small">Building</span>
                        <span class="fw-semibold small">@Model.IncidentType</span>
                    </div>
                    <div class="d-flex justify-content-between mb-1">
                        <span class="text-muted small">Status</span>
                        <span class="badge bg-warning text-dark small">Pending</span>
                    </div>
                    <div class="d-flex justify-content-between">
                        <span class="text-muted small">Submitted</span>
                        <span class="fw-semibold small">
                            @Model.ReportedAt.ToString("MMM dd, yyyy h:mm tt")
                        </span>
                    </div>
                </div>
                <a href="/SafetyReport/Submit"
                   class="btn btn-warning me-2">
                    <i class="bi bi-plus-circle me-1"></i>Submit Another
                </a>
                <a href="/" class="btn btn-outline-secondary">
                    <i class="bi bi-house me-1"></i>Home
                </a>
            </div>
        </div>
    </div>
</div>
```

- [ ] **Step 2: Build to verify**

```
dotnet build EduConnect.Web
```
Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 3: Commit**

```
git add EduConnect.Web/Views/SafetyReport/Confirmation.cshtml
git commit -m "feat: add SafetyReport/Confirmation view"
```

---

## Task 5: StaffController

**Files:**
- Create: `EduConnect.Web/Controllers/StaffController.cs`

- [ ] **Step 1: Create the file**

```csharp
using EduConnect.Web.Data;
using EduConnect.Web.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EduConnect.Web.Controllers
{
    public class StaffController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<StaffController> _logger;

        public StaffController(
            ApplicationDbContext context,
            ILogger<StaffController> logger)
        {
            _context = context;
            _logger = logger;
        }

        private bool IsStaffOrAdmin()
        {
            var role = HttpContext.Session.GetString("RoleName");
            return role == "Staff" || role == "Administrator";
        }

        private int GetUserID() =>
            int.Parse(HttpContext.Session.GetString("UserID")!);

        // GET /Staff
        public async Task<IActionResult> Index(
            SafetyReportFilterViewModel filter)
        {
            if (!IsStaffOrAdmin())
                return RedirectToAction("Login", "Account");

            var query = _context.IncidentReports
                .Include(r => r.ReportedBy)
                .AsQueryable();

            if (!string.IsNullOrEmpty(filter.Building))
                query = query.Where(r =>
                    r.IncidentType == filter.Building);

            if (!string.IsNullOrEmpty(filter.Status))
                query = query.Where(r =>
                    r.Status == filter.Status);

            var reports = await query
                .OrderByDescending(r => r.ReportedAt)
                .ToListAsync();

            ViewBag.Filter = filter;
            return View(reports);
        }

        // GET /Staff/ReportDetails/{id}
        public async Task<IActionResult> ReportDetails(int id)
        {
            if (!IsStaffOrAdmin())
                return RedirectToAction("Login", "Account");

            var report = await _context.IncidentReports
                .Include(r => r.ReportedBy)
                .Include(r => r.HandledBy)
                .FirstOrDefaultAsync(r => r.ReportID == id);

            if (report == null) return NotFound();

            return View(report);
        }

        // POST /Staff/UpdateStatus/{id}
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateStatus(
            int id, string status, string? resolution)
        {
            if (!IsStaffOrAdmin())
                return RedirectToAction("Login", "Account");

            var report = await _context.IncidentReports
                .FirstOrDefaultAsync(r => r.ReportID == id);

            if (report == null) return NotFound();

            report.Status = status;
            report.Resolution = resolution;
            report.HandledByID = GetUserID();

            if (status == "Resolved" || status == "Dismissed")
                report.ResolvedAt = DateTime.Now;

            await _context.SaveChangesAsync();

            TempData["Success"] = "Report status updated.";
            return RedirectToAction("ReportDetails", new { id });
        }
    }
}
```

- [ ] **Step 2: Build to verify**

```
dotnet build EduConnect.Web
```
Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 3: Commit**

```
git add EduConnect.Web/Controllers/StaffController.cs
git commit -m "feat: add StaffController for safety report management"
```

---

## Task 6: Staff/Index view

**Files:**
- Create: `EduConnect.Web/Views/Staff/Index.cshtml`

- [ ] **Step 1: Create the Views/Staff directory and file**

```cshtml
@model IEnumerable<EduConnect.Web.Models.IncidentReport>
@using EduConnect.Web.ViewModels
@{
    ViewData["Title"] = "Safety Reports";
    var filter = ViewBag.Filter as SafetyReportFilterViewModel
                 ?? new SafetyReportFilterViewModel();
}

<!-- Page Header -->
<div class="d-flex justify-content-between align-items-center mb-4">
    <div>
        <h4 class="fw-bold mb-1">
            <i class="bi bi-shield-exclamation me-2 text-warning"></i>
            Safety Reports
        </h4>
        <small class="text-muted">
            @Model.Count() report(s) found
        </small>
    </div>
</div>

@if (TempData["Success"] != null)
{
    <div class="alert alert-success alert-dismissible fade show mb-4" role="alert">
        <i class="bi bi-check-circle me-2"></i>@TempData["Success"]
        <button type="button" class="btn-close" data-bs-dismiss="alert"></button>
    </div>
}

<!-- Filters -->
<div class="card border-0 shadow-sm mb-4">
    <div class="card-body p-3">
        <form method="get" asp-action="Index" class="row g-2 align-items-end">
            <div class="col-12 col-sm-4">
                <label class="form-label small fw-semibold mb-1">Building</label>
                <select name="Building" class="form-select form-select-sm">
                    <option value="">All Buildings</option>
                    <option value="SV"  @(filter.Building == "SV"  ? "selected" : "")>SV</option>
                    <option value="ST"  @(filter.Building == "ST"  ? "selected" : "")>ST</option>
                    <option value="OZ"  @(filter.Building == "OZ"  ? "selected" : "")>OZ</option>
                    <option value="CS"  @(filter.Building == "CS"  ? "selected" : "")>CS</option>
                    <option value="CT"  @(filter.Building == "CT"  ? "selected" : "")>CT</option>
                    <option value="Other" @(filter.Building == "Other" ? "selected" : "")>Other</option>
                </select>
            </div>
            <div class="col-12 col-sm-4">
                <label class="form-label small fw-semibold mb-1">Status</label>
                <select name="Status" class="form-select form-select-sm">
                    <option value="">All Statuses</option>
                    <option value="Pending"      @(filter.Status == "Pending"      ? "selected" : "")>Pending</option>
                    <option value="Investigating" @(filter.Status == "Investigating" ? "selected" : "")>Investigating</option>
                    <option value="Resolved"     @(filter.Status == "Resolved"     ? "selected" : "")>Resolved</option>
                    <option value="Dismissed"    @(filter.Status == "Dismissed"    ? "selected" : "")>Dismissed</option>
                </select>
            </div>
            <div class="col-12 col-sm-4">
                <button type="submit" class="btn btn-primary btn-sm w-100">
                    <i class="bi bi-funnel me-1"></i>Filter
                </button>
            </div>
        </form>
    </div>
</div>

<!-- Reports Table -->
<div class="card border-0 shadow-sm">
    <div class="card-body p-0">
        @if (!Model.Any())
        {
            <div class="text-center text-muted py-5">
                <i class="bi bi-inbox fs-1 d-block mb-2"></i>
                No reports match the current filters.
            </div>
        }
        else
        {
            <div class="table-responsive">
                <table class="table table-hover mb-0">
                    <thead class="table-light">
                        <tr>
                            <th class="ps-4">#</th>
                            <th>Building</th>
                            <th>Location</th>
                            <th>Reporter</th>
                            <th>Submitted</th>
                            <th>Status</th>
                            <th></th>
                        </tr>
                    </thead>
                    <tbody>
                        @foreach (var r in Model)
                        {
                            <tr>
                                <td class="ps-4 text-muted small">@r.ReportID</td>
                                <td>
                                    <span class="badge bg-secondary">
                                        @r.IncidentType
                                    </span>
                                </td>
                                <td class="small">
                                    @(string.IsNullOrWhiteSpace(r.Location)
                                        ? "—"
                                        : r.Location)
                                </td>
                                <td class="small">
                                    @if (r.IsAnonymous)
                                    {
                                        <span class="text-muted fst-italic">
                                            Anonymous
                                        </span>
                                    }
                                    else
                                    {
                                        @(r.ReportedBy != null
                                            ? $"{r.ReportedBy.FirstName} {r.ReportedBy.LastName}"
                                            : "—")
                                    }
                                </td>
                                <td class="small text-muted">
                                    @r.ReportedAt.ToString("MMM dd, yyyy")
                                </td>
                                <td>
                                    @{
                                        var (badgeClass, badgeLabel) = r.Status switch
                                        {
                                            "Pending"       => ("bg-warning text-dark", "Pending"),
                                            "Investigating" => ("bg-primary",           "Investigating"),
                                            "Resolved"      => ("bg-success",           "Resolved"),
                                            "Dismissed"     => ("bg-secondary",         "Dismissed"),
                                            _               => ("bg-light text-dark",   r.Status)
                                        };
                                    }
                                    <span class="badge @badgeClass">@badgeLabel</span>
                                </td>
                                <td>
                                    <a href="/Staff/ReportDetails/@r.ReportID"
                                       class="btn btn-sm btn-outline-primary">
                                        View
                                    </a>
                                </td>
                            </tr>
                        }
                    </tbody>
                </table>
            </div>
        }
    </div>
</div>
```

- [ ] **Step 2: Build to verify**

```
dotnet build EduConnect.Web
```
Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 3: Commit**

```
git add EduConnect.Web/Views/Staff/Index.cshtml
git commit -m "feat: add Staff/Index view with building and status filters"
```

---

## Task 7: Staff/ReportDetails view

**Files:**
- Create: `EduConnect.Web/Views/Staff/ReportDetails.cshtml`

- [ ] **Step 1: Create the file**

```cshtml
@model EduConnect.Web.Models.IncidentReport
@{
    ViewData["Title"] = $"Report #{Model.ReportID}";
    var (badgeClass, badgeLabel) = Model.Status switch
    {
        "Pending"       => ("bg-warning text-dark", "Pending"),
        "Investigating" => ("bg-primary",           "Investigating"),
        "Resolved"      => ("bg-success",           "Resolved"),
        "Dismissed"     => ("bg-secondary",         "Dismissed"),
        _               => ("bg-light text-dark",   Model.Status)
    };
}

<!-- Header -->
<div class="d-flex justify-content-between align-items-center mb-4">
    <div>
        <h4 class="fw-bold mb-1">
            <i class="bi bi-file-earmark-text me-2 text-warning"></i>
            Report #@Model.ReportID
        </h4>
        <small class="text-muted">
            <span class="badge bg-secondary me-1">@Model.IncidentType</span>
            <span class="badge @badgeClass">@badgeLabel</span>
        </small>
    </div>
    <a href="/Staff" class="btn btn-outline-secondary btn-sm">
        <i class="bi bi-arrow-left me-2"></i>Back to Reports
    </a>
</div>

@if (TempData["Success"] != null)
{
    <div class="alert alert-success alert-dismissible fade show mb-4" role="alert">
        <i class="bi bi-check-circle me-2"></i>@TempData["Success"]
        <button type="button" class="btn-close" data-bs-dismiss="alert"></button>
    </div>
}

<div class="row g-4">

    <!-- Left: Report Details -->
    <div class="col-12 col-lg-7">
        <div class="card border-0 shadow-sm h-100">
            <div class="card-body p-4">

                <h6 class="text-muted text-uppercase fw-bold mb-3 small">
                    Report Details
                </h6>

                <dl class="row mb-0">
                    <dt class="col-sm-4 text-muted small">Building</dt>
                    <dd class="col-sm-8">
                        <span class="badge bg-secondary">@Model.IncidentType</span>
                    </dd>

                    <dt class="col-sm-4 text-muted small">Specific Location</dt>
                    <dd class="col-sm-8 small">
                        @(string.IsNullOrWhiteSpace(Model.Location)
                            ? "Not specified"
                            : Model.Location)
                    </dd>

                    <dt class="col-sm-4 text-muted small">Reporter</dt>
                    <dd class="col-sm-8 small">
                        @if (Model.IsAnonymous)
                        {
                            <span class="text-muted fst-italic">Anonymous</span>
                        }
                        else
                        {
                            @(Model.ReportedBy != null
                                ? $"{Model.ReportedBy.FirstName} {Model.ReportedBy.LastName}"
                                : "—")
                        }
                    </dd>

                    <dt class="col-sm-4 text-muted small">Submitted</dt>
                    <dd class="col-sm-8 small">
                        @Model.ReportedAt.ToString("MMM dd, yyyy h:mm tt")
                    </dd>

                    @if (Model.ResolvedAt.HasValue)
                    {
                        <dt class="col-sm-4 text-muted small">Resolved / Dismissed</dt>
                        <dd class="col-sm-8 small">
                            @Model.ResolvedAt.Value.ToString("MMM dd, yyyy h:mm tt")
                        </dd>
                    }

                    @if (Model.HandledBy != null)
                    {
                        <dt class="col-sm-4 text-muted small">Handled By</dt>
                        <dd class="col-sm-8 small">
                            @Model.HandledBy.FirstName @Model.HandledBy.LastName
                        </dd>
                    }
                </dl>

                <hr class="my-3" />

                <h6 class="text-muted text-uppercase fw-bold mb-2 small">
                    Description
                </h6>
                <p class="mb-0" style="white-space:pre-wrap">@Model.Description</p>

                @if (!string.IsNullOrWhiteSpace(Model.Resolution))
                {
                    <hr class="my-3" />
                    <h6 class="text-muted text-uppercase fw-bold mb-2 small">
                        Resolution Note
                    </h6>
                    <p class="mb-0 text-success" style="white-space:pre-wrap">
                        @Model.Resolution
                    </p>
                }

            </div>
        </div>
    </div>

    <!-- Right: Photo + Update Form -->
    <div class="col-12 col-lg-5">

        @if (!string.IsNullOrEmpty(Model.PhotoURL))
        {
            <div class="card border-0 shadow-sm mb-4">
                <div class="card-body p-3">
                    <h6 class="text-muted text-uppercase fw-bold mb-2 small">
                        Photo
                    </h6>
                    <img src="@Model.PhotoURL"
                         alt="Incident photo"
                         class="img-fluid rounded"
                         style="max-height:300px;object-fit:cover;width:100%" />
                </div>
            </div>
        }

        <div class="card border-0 shadow-sm">
            <div class="card-body p-4">
                <h6 class="text-muted text-uppercase fw-bold mb-3 small">
                    Update Status
                </h6>

                <form asp-action="UpdateStatus"
                      asp-route-id="@Model.ReportID"
                      method="post">
                    @Html.AntiForgeryToken()

                    <div class="mb-3">
                        <label class="form-label fw-semibold small">Status</label>
                        <select name="status" class="form-select">
                            <option value="Pending"
                                    @(Model.Status == "Pending" ? "selected" : "")>
                                Pending
                            </option>
                            <option value="Investigating"
                                    @(Model.Status == "Investigating" ? "selected" : "")>
                                Investigating
                            </option>
                            <option value="Resolved"
                                    @(Model.Status == "Resolved" ? "selected" : "")>
                                Resolved
                            </option>
                            <option value="Dismissed"
                                    @(Model.Status == "Dismissed" ? "selected" : "")>
                                Dismissed
                            </option>
                        </select>
                    </div>

                    <div class="mb-3">
                        <label class="form-label fw-semibold small">
                            Resolution Note
                        </label>
                        <textarea name="resolution"
                                  class="form-control"
                                  rows="4"
                                  placeholder="Describe what was done or why it was dismissed...">@Model.Resolution</textarea>
                    </div>

                    <div class="d-grid">
                        <button type="submit" class="btn btn-primary fw-semibold">
                            <i class="bi bi-check2 me-2"></i>Save Update
                        </button>
                    </div>

                </form>
            </div>
        </div>

    </div>
</div>
```

- [ ] **Step 2: Build to verify**

```
dotnet build EduConnect.Web
```
Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 3: Commit**

```
git add EduConnect.Web/Views/Staff/ReportDetails.cshtml
git commit -m "feat: add Staff/ReportDetails view with photo and status update form"
```

---

## Task 8: Navigation — _Layout.cshtml

**Files:**
- Modify: `EduConnect.Web/Views/Shared/_Layout.cshtml`

This task makes **three changes** to the sidebar's role-based nav:

1. **Administrator branch** — add "Safety Reports" management link
2. **All existing branches (Dean, Faculty, else/Student)** — add "Report Safety Issue" link
3. **New Staff branch** — add a full Staff sidebar with "Safety Reports" + "Report Safety Issue" links (this fixes the broken `Staff/Index` redirect)

- [ ] **Step 1: Add "Safety Reports" link to the Administrator branch**

Locate the Administrator sidebar block. After the existing Notifications `<li>`:

```cshtml
<!-- existing: -->
<li class="nav-item">
    <a href="/Notification"
       class="nav-link text-dark">
        <i class="bi bi-bell me-2"></i>
        Notifications
    </a>
</li>
```

Add immediately after that closing `</li>`:

```cshtml
<li class="nav-item">
    <a href="/Staff"
       class="nav-link text-dark">
        <i class="bi bi-shield-exclamation me-2"></i>
        Safety Reports
    </a>
</li>
<li class="nav-item">
    <a href="/SafetyReport/Submit"
       class="nav-link text-dark">
        <i class="bi bi-flag me-2"></i>
        Report Safety Issue
    </a>
</li>
```

- [ ] **Step 2: Add "Report Safety Issue" to Dean branch**

Locate the Dean sidebar block. After the Dean's Notifications `<li>`, add:

```cshtml
<li class="nav-item">
    <a href="/SafetyReport/Submit"
       class="nav-link text-dark">
        <i class="bi bi-flag me-2"></i>
        Report Safety Issue
    </a>
</li>
```

- [ ] **Step 3: Add "Report Safety Issue" to Faculty branch**

Locate the Faculty sidebar block. After the Faculty's Notifications `<li>`, add:

```cshtml
<li class="nav-item">
    <a href="/SafetyReport/Submit"
       class="nav-link text-dark">
        <i class="bi bi-flag me-2"></i>
        Report Safety Issue
    </a>
</li>
```

- [ ] **Step 4: Add Staff branch before the `else` (Student) block**

Find this line in the sidebar nav chain:

```cshtml
                else
                {
                    <!-- Student -->
```

Insert a new `else if` block immediately before it:

```cshtml
                else if (currentRole == "Staff")
                {
                    <li class="nav-item">
                        <a href="/Staff"
                           class="nav-link text-dark">
                            <i class="bi bi-speedometer2 me-2"></i>
                            Dashboard
                        </a>
                    </li>
                    <li class="nav-item">
                        <a href="/Staff"
                           class="nav-link text-dark">
                            <i class="bi bi-shield-exclamation me-2"></i>
                            Safety Reports
                        </a>
                    </li>
                    <li class="nav-item">
                        <a href="/SafetyReport/Submit"
                           class="nav-link text-dark">
                            <i class="bi bi-flag me-2"></i>
                            Report Safety Issue
                        </a>
                    </li>
                    <li class="nav-item">
                        <a href="/Announcement"
                           class="nav-link text-dark">
                            <i class="bi bi-megaphone me-2"></i>
                            Announcements
                        </a>
                    </li>
                    <li class="nav-item">
                        <a href="/Event"
                           class="nav-link text-dark">
                            <i class="bi bi-calendar-event me-2"></i>
                            Events
                        </a>
                    </li>
                    <li class="nav-item">
                        <a href="/Notification"
                           class="nav-link text-dark">
                            <i class="bi bi-bell me-2"></i>
                            Notifications
                        </a>
                    </li>
                }
```

- [ ] **Step 5: Add "Report Safety Issue" to the Student (else) branch**

Inside the `else` block, after the existing Notifications `<li>`, add:

```cshtml
<li class="nav-item">
    <a href="/SafetyReport/Submit"
       class="nav-link text-dark">
        <i class="bi bi-flag me-2"></i>
        Report Safety Issue
    </a>
</li>
```

- [ ] **Step 6: Build to verify**

```
dotnet build EduConnect.Web
```
Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 7: Commit**

```
git add EduConnect.Web/Views/Shared/_Layout.cshtml
git commit -m "feat: add Safety Reports and Report Safety Issue nav links; add Staff sidebar branch"
```

---

## Task 9: Smoke Test

No automated tests exist in this project. Verify each flow manually.

- [ ] **Step 1: Run the app**

```
dotnet run --project EduConnect.Web
```
Navigate to `https://localhost:7135`

- [ ] **Step 2: Test submission flow (as any logged-in user)**

1. Log in as any verified user
2. Click "Report Safety Issue" in the sidebar
3. Select a building (e.g., SV), enter a specific location, write a description
4. Attach a JPG photo under 5 MB
5. Leave "Submit anonymously" unchecked
6. Click "Submit Report"
7. Verify the Confirmation page shows the correct Report ID, building, and timestamp

- [ ] **Step 3: Test anonymous submission**

1. Repeat Task 9 Step 2 but check "Submit anonymously"
2. Log in as a Staff user and go to `/Staff`
3. Find the report — Reporter column should show "Anonymous"

- [ ] **Step 4: Test staff management panel (as Staff or Administrator)**

1. Log in as a Staff or Administrator user
2. Click "Safety Reports" in the sidebar — should load `/Staff` with the report list
3. Use the Building filter (e.g., SV) — verify only SV reports appear
4. Use the Status filter (Pending) — verify only Pending reports appear
5. Click "View" on a report — should load the ReportDetails page with photo displayed

- [ ] **Step 5: Test status update**

1. On the ReportDetails page, change Status to "Investigating", add a resolution note
2. Click "Save Update"
3. Verify the page reloads with the updated status badge and resolution note shown

- [ ] **Step 6: Test photo validation**

1. Go to `/SafetyReport/Submit`
2. Attach a file with a `.gif` extension
3. Submit — verify the form returns with the error "Photo must be a JPG or PNG under 5 MB."

- [ ] **Step 7: Test unauthenticated access**

1. Log out
2. Navigate directly to `https://localhost:7135/SafetyReport/Submit`
3. Verify redirect to Login page
4. Navigate directly to `https://localhost:7135/Staff`
5. Verify redirect to Login page

- [ ] **Step 8: Commit final state**

```
git add .
git commit -m "feat: campus safety reporting — submission, staff panel, notifications"
```
