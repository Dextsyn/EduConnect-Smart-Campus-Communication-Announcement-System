# Announcement Approval Flow Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a Faculty → Chair Person → Dean two-stage approval pipeline so Faculty can draft announcements, submit them for review, and self-publish once both reviewers approve.

**Architecture:** Extend `ApprovalStatus` on `Announcements` with five states (`Draft`, `PendingChair`, `PendingDean`, `Approved`, `Rejected`), add three new nullable columns for Chair Person tracking, and wire up six new controller actions on `AnnouncementController`. Email + in-app notifications fire at each state transition via existing `IEmailService` / `INotificationService`.

**Tech Stack:** ASP.NET Core 8 MVC, EF Core (SQL Server), session-based auth, MailKit via `IEmailService`, SignalR via `INotificationService`, Bootstrap 5 + Bootstrap Icons.

**Spec:** `docs/superpowers/specs/2026-06-09-announcement-approval-flow-design.md`

---

## File Map

| File | Action | Purpose |
|---|---|---|
| `EduConnect.Web/Models/Announcement.cs` | Modify | Add 3 new columns + nav property |
| `EduConnect.Web/Data/ApplicationDbContext.cs` | Modify | Add FK config for `ChairApprovedBy` |
| `EduConnect.Web/Migrations/<timestamp>_AddAnnouncementApprovalFlow.cs` | Create (generated) | Migration for 3 new columns |
| `EduConnect.Web/Controllers/AnnouncementController.cs` | Modify | Inject `IEmailService`, update Create, Edit; add Submit, Publish, MyAnnouncements, ReviewQueue, Review, Approve, Reject |
| `EduConnect.Web/Views/Announcement/Create.cshtml` | Modify | Swap "Publish" button to "Save Draft" for Faculty |
| `EduConnect.Web/Views/Announcement/MyAnnouncements.cshtml` | Create | Faculty list with status badges + action buttons |
| `EduConnect.Web/Views/Announcement/ReviewQueue.cshtml` | Create | Shared Chair/Dean pending review list |
| `EduConnect.Web/Views/Announcement/Review.cshtml` | Create | Full announcement preview with Approve/Reject form |
| `EduConnect.Web/Views/Shared/_Layout.cshtml` | Modify | Add Chair Person sidebar block; add My Announcements to Faculty; add Pending Reviews to Dean |

---

## Task 1: Schema — Add 3 Columns + FK Config + Migration

**Files:**
- Modify: `EduConnect.Web/Models/Announcement.cs`
- Modify: `EduConnect.Web/Data/ApplicationDbContext.cs`

- [ ] **Step 1: Add three new properties + navigation property to Announcement**

In `EduConnect.Web/Models/Announcement.cs`, add these four members immediately after the existing `RejectionReason` line (line 39):

```csharp
public string? ChairRejectionReason { get; set; }
public int? ChairApprovedByID { get; set; }
public DateTime? ChairApprovedAt { get; set; }

// Navigation property
public User? ChairApprovedBy { get; set; }
```

- [ ] **Step 2: Add FK configuration for ChairApprovedBy in ApplicationDbContext**

In `EduConnect.Web/Data/ApplicationDbContext.cs`, add a new `modelBuilder.Entity<Announcement>` block directly after the existing `ApprovedBy` FK block (around line 257):

```csharp
// Announcement chairapprovedby relationship
modelBuilder.Entity<Announcement>(entity =>
{
    entity.HasOne(e => e.ChairApprovedBy)
          .WithMany()
          .HasForeignKey(e => e.ChairApprovedByID)
          .OnDelete(DeleteBehavior.Restrict);
});
```

- [ ] **Step 3: Generate and apply the migration**

```bash
dotnet ef migrations add AddAnnouncementApprovalFlow --project EduConnect.Web
dotnet ef database update --project EduConnect.Web
```

Expected output ends with: `Done.`

- [ ] **Step 4: Verify the migration file**

Open the newly created migration file under `EduConnect.Web/Migrations/` and confirm it contains three `AddColumn` calls for `ChairRejectionReason`, `ChairApprovedByID`, and `ChairApprovedAt` on the `Announcements` table, plus a `CreateIndex` or `AddForeignKey` for `ChairApprovedByID`.

- [ ] **Step 5: Commit**

```bash
git add EduConnect.Web/Models/Announcement.cs EduConnect.Web/Data/ApplicationDbContext.cs EduConnect.Web/Migrations/
git commit -m "feat: add ChairApproved columns to Announcements for two-stage approval"
```

---

## Task 2: Inject IEmailService + Update Create (Faculty Path)

**Files:**
- Modify: `EduConnect.Web/Controllers/AnnouncementController.cs`

- [ ] **Step 1: Inject IEmailService into AnnouncementController**

Replace the constructor and field declarations (starting from line 12) with:

```csharp
private readonly ApplicationDbContext _context;
private readonly ILogger<AnnouncementController> _logger;
private readonly IWebHostEnvironment _environment;
private readonly INotificationService _notificationService;
private readonly IEmailService _emailService;

public AnnouncementController(
    ApplicationDbContext context,
    ILogger<AnnouncementController> logger,
    IWebHostEnvironment environment,
    INotificationService notificationService,
    IEmailService emailService)
{
    _context = context;
    _logger = logger;
    _environment = environment;
    _notificationService = notificationService;
    _emailService = emailService;
}
```

- [ ] **Step 2: Add CanCreate and IsFaculty helpers**

After the existing `CanEditAnnouncement` helper, add:

```csharp
private bool IsFaculty() =>
    GetRoleName() == "Faculty";

private bool CanCreate()
{
    var role = GetRoleName();
    return role == "Administrator" ||
           role == "Dean" ||
           role == "Chair Person" ||
           role == "Faculty";
}
```

- [ ] **Step 3: Update Create GET to allow Faculty**

In `GET /Announcement/Create`, replace the guard:
```csharp
if (!CanPublish())
    return RedirectToAction("Index");
```
with:
```csharp
if (!CanCreate())
    return RedirectToAction("Index");
```

Also add this line to `ViewBag` assignments before `return View(model)`:
```csharp
ViewBag.IsFaculty = IsFaculty();
```

- [ ] **Step 4: Update Create POST to save Faculty announcements as Draft**

In `POST /Announcement/Create`, replace the guard:
```csharp
if (!CanPublish())
    return RedirectToAction("Index");
```
with:
```csharp
if (!CanCreate())
    return RedirectToAction("Index");
```

Then replace the block that sets `approvalStatus`, `status`, `publishedAt`, and `submittedAt` (currently around line 419–422):

```csharp
// Faculty saves as Draft; everyone else publishes directly
string approvalStatus;
string status;
DateTime? publishedAt;

if (IsFaculty())
{
    approvalStatus = "Draft";
    status = "Draft";
    publishedAt = null;
}
else
{
    approvalStatus = "Approved";
    status = "Published";
    publishedAt = DateTime.Now;
}
```

At the very end of the POST action, replace the single redirect:
```csharp
TempData["Success"] =
    "Announcement published successfully!";

return RedirectToAction("Index");
```
with:
```csharp
if (IsFaculty())
{
    TempData["Success"] =
        "Draft saved. Submit it for review when ready.";
    return RedirectToAction("MyAnnouncements");
}

TempData["Success"] =
    "Announcement published successfully!";
return RedirectToAction("Index");
```

Also add `ViewBag.IsFaculty = IsFaculty();` in the validation-failed reload blocks (both times `return View(model)` is hit) so the view renders correctly on error.

- [ ] **Step 5: Build and confirm no compile errors**

```bash
dotnet build EduConnect.Web
```

Expected: `Build succeeded.`

- [ ] **Step 6: Commit**

```bash
git add EduConnect.Web/Controllers/AnnouncementController.cs
git commit -m "feat: allow Faculty to create announcements saved as Draft"
```

---

## Task 3: Update Create View — "Save Draft" Button for Faculty

**Files:**
- Modify: `EduConnect.Web/Views/Announcement/Create.cshtml`

- [ ] **Step 1: Replace the static submit button with a role-aware button**

Find the submit button block in `Create.cshtml` (around line 253):
```html
<button type="submit"
        class="btn btn-primary btn-lg">
    <i class="bi bi-send me-2"></i>
    Publish Announcement
```

Replace it with:
```html
<button type="submit"
        class="btn btn-primary btn-lg">
    <i class="bi bi-send me-2"></i>
    @(ViewBag.IsFaculty == true ? "Save Draft" : "Publish Announcement")
```

- [ ] **Step 2: Verify manually**

Run the app (`dotnet run --project EduConnect.Web`), log in as Faculty, go to `/Announcement/Create`. The button should read "Save Draft". Log in as Admin or Dean — button should read "Publish Announcement".

- [ ] **Step 3: Commit**

```bash
git add EduConnect.Web/Views/Announcement/Create.cshtml
git commit -m "feat: show Save Draft button on Create form for Faculty role"
```

---

## Task 4: Add MyAnnouncements Action + View

**Files:**
- Modify: `EduConnect.Web/Controllers/AnnouncementController.cs`
- Create: `EduConnect.Web/Views/Announcement/MyAnnouncements.cshtml`

- [ ] **Step 1: Add the MyAnnouncements GET action**

Add this action to `AnnouncementController`, after the `Delete` action:

```csharp
// ═══════════════════════════════════════
//  GET: /Announcement/MyAnnouncements
//  Faculty's own announcement list
// ═══════════════════════════════════════
public async Task<IActionResult> MyAnnouncements()
{
    if (!IsLoggedIn())
        return RedirectToAction("Login", "Account");

    if (!IsFaculty())
        return RedirectToAction("Index");

    var userID = GetUserID();

    var announcements = await _context.Announcements
        .Include(a => a.Category)
        .Where(a => a.AuthorID == userID)
        .OrderByDescending(a => a.CreatedAt)
        .Select(a => new
        {
            a.AnnouncementID,
            a.Title,
            a.FeedType,
            a.Status,
            a.ApprovalStatus,
            a.SubmittedAt,
            a.CreatedAt,
            a.ChairRejectionReason,
            a.RejectionReason,
            CategoryName = a.Category.CategoryName
        })
        .ToListAsync();

    ViewBag.Announcements = announcements;
    return View();
}
```

- [ ] **Step 2: Create the MyAnnouncements view**

Create `EduConnect.Web/Views/Announcement/MyAnnouncements.cshtml`:

```cshtml
@{
    ViewData["Title"] = "My Announcements";
    var announcements = ViewBag.Announcements as IEnumerable<dynamic>;
}

<div class="d-flex justify-content-between align-items-center mb-4">
    <h4 class="fw-bold mb-0">
        <i class="bi bi-file-earmark-text me-2"></i>My Announcements
    </h4>
    <a href="/Announcement/Create" class="btn btn-primary">
        <i class="bi bi-plus-circle me-2"></i>New Draft
    </a>
</div>

@if (TempData["Success"] != null)
{
    <div class="alert alert-success alert-dismissible fade show">
        @TempData["Success"]
        <button type="button" class="btn-close" data-bs-dismiss="alert"></button>
    </div>
}
@if (TempData["Error"] != null)
{
    <div class="alert alert-danger alert-dismissible fade show">
        @TempData["Error"]
        <button type="button" class="btn-close" data-bs-dismiss="alert"></button>
    </div>
}

<div class="card shadow-sm">
    <div class="card-body p-0">
        @if (announcements == null || !((IEnumerable<dynamic>)announcements).Any())
        {
            <div class="text-center text-muted py-5">
                <i class="bi bi-file-earmark-x fs-1 d-block mb-2"></i>
                No announcements yet. <a href="/Announcement/Create">Create your first draft.</a>
            </div>
        }
        else
        {
            <table class="table table-hover mb-0">
                <thead class="table-light">
                    <tr>
                        <th>Title</th>
                        <th>Category</th>
                        <th>Feed</th>
                        <th>Status</th>
                        <th>Submitted</th>
                        <th>Actions</th>
                    </tr>
                </thead>
                <tbody>
                    @foreach (var a in announcements)
                    {
                        string approvalStatus = a.ApprovalStatus;
                        string badgeClass = approvalStatus switch
                        {
                            "Draft"        => "bg-secondary",
                            "PendingChair" => "bg-warning text-dark",
                            "PendingDean"  => "bg-orange text-dark",
                            "Approved"     => "bg-success",
                            "Rejected"     => "bg-danger",
                            _              => "bg-secondary"
                        };
                        string badgeLabel = approvalStatus switch
                        {
                            "PendingChair" => "Pending Chair",
                            "PendingDean"  => "Pending Dean",
                            _              => approvalStatus
                        };

                        <tr>
                            <td class="align-middle fw-semibold">@a.Title</td>
                            <td class="align-middle">@a.CategoryName</td>
                            <td class="align-middle">@a.FeedType</td>
                            <td class="align-middle">
                                <span class="badge @badgeClass">@badgeLabel</span>
                                @if (approvalStatus == "Rejected")
                                {
                                    string reason = !string.IsNullOrEmpty((string)a.ChairRejectionReason)
                                        ? a.ChairRejectionReason
                                        : a.RejectionReason;
                                    if (!string.IsNullOrEmpty(reason))
                                    {
                                        <div class="small text-danger mt-1">
                                            <i class="bi bi-x-circle me-1"></i>@reason
                                        </div>
                                    }
                                }
                            </td>
                            <td class="align-middle small text-muted">
                                @(a.SubmittedAt != null
                                    ? ((DateTime)a.SubmittedAt).ToString("MMM dd, yyyy")
                                    : "—")
                            </td>
                            <td class="align-middle">
                                <div class="d-flex gap-2 flex-wrap">
                                    @if (approvalStatus == "Draft" || approvalStatus == "Rejected")
                                    {
                                        <a href="/Announcement/Edit/@a.AnnouncementID"
                                           class="btn btn-sm btn-outline-secondary">
                                            <i class="bi bi-pencil"></i> Edit
                                        </a>
                                    }
                                    @if (approvalStatus == "Draft")
                                    {
                                        <form method="post"
                                              action="/Announcement/Submit/@a.AnnouncementID"
                                              style="display:inline;">
                                            @Html.AntiForgeryToken()
                                            <button type="submit" class="btn btn-sm btn-primary">
                                                <i class="bi bi-send"></i> Submit for Review
                                            </button>
                                        </form>
                                    }
                                    @if (approvalStatus == "Approved" && (string)a.Status != "Published")
                                    {
                                        <form method="post"
                                              action="/Announcement/Publish/@a.AnnouncementID"
                                              style="display:inline;">
                                            @Html.AntiForgeryToken()
                                            <button type="submit" class="btn btn-sm btn-success">
                                                <i class="bi bi-broadcast"></i> Publish
                                            </button>
                                        </form>
                                    }
                                    @if ((string)a.Status == "Published")
                                    {
                                        <span class="badge bg-success">Live</span>
                                    }
                                </div>
                            </td>
                        </tr>
                    }
                </tbody>
            </table>
        }
    </div>
</div>
```

- [ ] **Step 3: Verify manually**

Run the app, log in as Faculty, navigate to `/Announcement/MyAnnouncements`. Should render the table (empty if no drafts yet). Non-Faculty roles should be redirected to `/Announcement`.

- [ ] **Step 4: Commit**

```bash
git add EduConnect.Web/Controllers/AnnouncementController.cs EduConnect.Web/Views/Announcement/MyAnnouncements.cshtml
git commit -m "feat: add MyAnnouncements page for Faculty to track draft and review status"
```

---

## Task 5: Add Submit Action

**Files:**
- Modify: `EduConnect.Web/Controllers/AnnouncementController.cs`

- [ ] **Step 1: Add the Submit POST action**

Add this action after `MyAnnouncements`:

```csharp
// ═══════════════════════════════════════
//  POST: /Announcement/Submit/{id}
//  Faculty submits draft for review
// ═══════════════════════════════════════
[HttpPost]
[ValidateAntiForgeryToken]
public async Task<IActionResult> Submit(int id)
{
    if (!IsLoggedIn())
        return RedirectToAction("Login", "Account");

    if (!IsFaculty())
        return RedirectToAction("Index");

    var userID = GetUserID();

    var announcement = await _context.Announcements
        .Include(a => a.AnnouncementTags)
        .FirstOrDefaultAsync(a =>
            a.AnnouncementID == id &&
            a.AuthorID == userID &&
            (a.ApprovalStatus == "Draft" ||
             a.ApprovalStatus == "Rejected"));

    if (announcement == null)
        return RedirectToAction("MyAnnouncements");

    // Find faculty's primary department tag
    var primaryDept = await _context.UserDepartments
        .FirstOrDefaultAsync(ud =>
            ud.UserID == userID && ud.IsPrimary);

    if (primaryDept == null)
    {
        TempData["Error"] =
            "No department assigned. Contact an administrator.";
        return RedirectToAction("MyAnnouncements");
    }

    // Find Chair Person in same department
    var reviewer = await _context.UserDepartments
        .Include(ud => ud.User)
            .ThenInclude(u => u.Role)
        .Where(ud =>
            ud.TagID == primaryDept.TagID &&
            ud.User.Role.RoleName == "Chair Person" &&
            ud.User.IsActive)
        .Select(ud => ud.User)
        .FirstOrDefaultAsync();

    string newApprovalStatus;

    if (reviewer != null)
    {
        newApprovalStatus = "PendingChair";
    }
    else
    {
        // Fall back to Dean
        reviewer = await _context.UserDepartments
            .Include(ud => ud.User)
                .ThenInclude(u => u.Role)
            .Where(ud =>
                ud.TagID == primaryDept.TagID &&
                ud.User.Role.RoleName == "Dean" &&
                ud.User.IsActive)
            .Select(ud => ud.User)
            .FirstOrDefaultAsync();

        if (reviewer == null)
        {
            TempData["Error"] =
                "No Chair Person or Dean found for your " +
                "department. Contact an administrator.";
            return RedirectToAction("MyAnnouncements");
        }

        newApprovalStatus = "PendingDean";
    }

    // Clear stale data from any prior rejected cycle
    announcement.ChairApprovedByID = null;
    announcement.ChairApprovedAt = null;
    announcement.ChairRejectionReason = null;
    announcement.ApprovedByID = null;
    announcement.ApprovedAt = null;
    announcement.RejectionReason = null;

    announcement.ApprovalStatus = newApprovalStatus;
    announcement.SubmittedAt = DateTime.Now;

    await _context.SaveChangesAsync();

    // In-app notification
    _ = _notificationService.SendAsync(
        reviewer.UserID,
        "AnnouncementReview",
        $"New announcement pending your review: {announcement.Title}",
        $"/Announcement/Review/{announcement.AnnouncementID}",
        announcement.AnnouncementID);

    // Email notification (fire-and-forget)
    _ = _emailService.SendEmailAsync(
        reviewer.Email,
        $"{reviewer.FirstName} {reviewer.LastName}",
        "EduConnect: Announcement Pending Review",
        $"<p>Hello {reviewer.FirstName},</p>" +
        $"<p>A new announcement requires your review: " +
        $"<strong>{announcement.Title}</strong></p>" +
        $"<p><a href='https://localhost:7135/Announcement/Review/" +
        $"{announcement.AnnouncementID}'>Click here to review</a></p>");

    TempData["Success"] =
        "Announcement submitted for review.";
    return RedirectToAction("MyAnnouncements");
}
```

- [ ] **Step 2: Build**

```bash
dotnet build EduConnect.Web
```

Expected: `Build succeeded.`

- [ ] **Step 3: Verify manually**

Log in as Faculty, create a draft, go to My Announcements, click "Submit for Review". The status badge should change to "Pending Chair" (or "Pending Dean" if no Chair exists in the department). The reviewer should receive an in-app notification.

- [ ] **Step 4: Commit**

```bash
git add EduConnect.Web/Controllers/AnnouncementController.cs
git commit -m "feat: add Submit action — Faculty submits draft to Chair/Dean for review"
```

---

## Task 6: Add ReviewQueue Action + View

**Files:**
- Modify: `EduConnect.Web/Controllers/AnnouncementController.cs`
- Create: `EduConnect.Web/Views/Announcement/ReviewQueue.cshtml`

- [ ] **Step 1: Add the ReviewQueue GET action**

```csharp
// ═══════════════════════════════════════
//  GET: /Announcement/ReviewQueue
//  Chair Person / Dean pending review list
// ═══════════════════════════════════════
public async Task<IActionResult> ReviewQueue()
{
    if (!IsLoggedIn())
        return RedirectToAction("Login", "Account");

    var roleName = GetRoleName();
    if (roleName != "Chair Person" && roleName != "Dean")
        return RedirectToAction("Index");

    var userID = GetUserID();

    var primaryDept = await _context.UserDepartments
        .FirstOrDefaultAsync(ud =>
            ud.UserID == userID && ud.IsPrimary);

    if (primaryDept == null)
    {
        ViewBag.Announcements = new List<object>();
        ViewBag.Role = roleName;
        return View();
    }

    var pendingStatus = roleName == "Chair Person"
        ? "PendingChair"
        : "PendingDean";

    var announcements = await _context.Announcements
        .Include(a => a.Author)
        .Include(a => a.Category)
        .Include(a => a.AnnouncementTags)
            .ThenInclude(at => at.DepartmentTag)
        .Where(a =>
            a.ApprovalStatus == pendingStatus &&
            a.AnnouncementTags.Any(at =>
                at.TagID == primaryDept.TagID))
        .OrderBy(a => a.SubmittedAt)
        .Select(a => new
        {
            a.AnnouncementID,
            a.Title,
            a.FeedType,
            a.SubmittedAt,
            AuthorName = a.Author.FirstName
                         + " " + a.Author.LastName,
            CategoryName = a.Category.CategoryName,
            Tags = a.AnnouncementTags
                .Select(at => at.DepartmentTag.ShortName)
                .ToList()
        })
        .ToListAsync();

    ViewBag.Announcements = announcements;
    ViewBag.Role = roleName;
    return View();
}
```

- [ ] **Step 2: Create the ReviewQueue view**

Create `EduConnect.Web/Views/Announcement/ReviewQueue.cshtml`:

```cshtml
@{
    ViewData["Title"] = "Pending Reviews";
    var announcements = ViewBag.Announcements as IEnumerable<dynamic>;
    string role = ViewBag.Role;
}

<div class="d-flex justify-content-between align-items-center mb-4">
    <h4 class="fw-bold mb-0">
        <i class="bi bi-hourglass-split me-2"></i>
        Pending Reviews
        <small class="text-muted fs-6 ms-2">
            (@(role == "Chair Person" ? "Chair Person Queue" : "Dean Queue"))
        </small>
    </h4>
</div>

@if (TempData["Success"] != null)
{
    <div class="alert alert-success alert-dismissible fade show">
        @TempData["Success"]
        <button type="button" class="btn-close" data-bs-dismiss="alert"></button>
    </div>
}

<div class="card shadow-sm">
    <div class="card-body p-0">
        @if (announcements == null || !((IEnumerable<dynamic>)announcements).Any())
        {
            <div class="text-center text-muted py-5">
                <i class="bi bi-check2-circle fs-1 d-block mb-2"></i>
                No announcements pending your review.
            </div>
        }
        else
        {
            <table class="table table-hover mb-0">
                <thead class="table-light">
                    <tr>
                        <th>Title</th>
                        <th>Author</th>
                        <th>Category</th>
                        <th>Feed</th>
                        <th>Submitted</th>
                        <th></th>
                    </tr>
                </thead>
                <tbody>
                    @foreach (var a in announcements)
                    {
                        <tr>
                            <td class="align-middle fw-semibold">@a.Title</td>
                            <td class="align-middle">@a.AuthorName</td>
                            <td class="align-middle">@a.CategoryName</td>
                            <td class="align-middle">@a.FeedType</td>
                            <td class="align-middle small text-muted">
                                @(a.SubmittedAt != null
                                    ? ((DateTime)a.SubmittedAt).ToString("MMM dd, yyyy")
                                    : "—")
                            </td>
                            <td class="align-middle">
                                <a href="/Announcement/Review/@a.AnnouncementID"
                                   class="btn btn-sm btn-outline-primary">
                                    <i class="bi bi-eye me-1"></i>Review
                                </a>
                            </td>
                        </tr>
                    }
                </tbody>
            </table>
        }
    </div>
</div>
```

- [ ] **Step 3: Verify manually**

Log in as Chair Person, navigate to `/Announcement/ReviewQueue`. Should show announcements submitted to that department with `PendingChair`. Log in as Dean — should show `PendingDean` items. Other roles redirect to `/Announcement`.

- [ ] **Step 4: Commit**

```bash
git add EduConnect.Web/Controllers/AnnouncementController.cs EduConnect.Web/Views/Announcement/ReviewQueue.cshtml
git commit -m "feat: add ReviewQueue action and view for Chair Person and Dean"
```

---

## Task 7: Add Review Action + View

**Files:**
- Modify: `EduConnect.Web/Controllers/AnnouncementController.cs`
- Create: `EduConnect.Web/Views/Announcement/Review.cshtml`

- [ ] **Step 1: Add the Review GET action**

```csharp
// ═══════════════════════════════════════
//  GET: /Announcement/Review/{id}
//  Full preview for reviewer
// ═══════════════════════════════════════
public async Task<IActionResult> Review(int id)
{
    if (!IsLoggedIn())
        return RedirectToAction("Login", "Account");

    var roleName = GetRoleName();
    if (roleName != "Chair Person" && roleName != "Dean")
        return RedirectToAction("Index");

    var userID = GetUserID();

    var primaryDept = await _context.UserDepartments
        .FirstOrDefaultAsync(ud =>
            ud.UserID == userID && ud.IsPrimary);

    if (primaryDept == null)
        return RedirectToAction("ReviewQueue");

    var expectedStatus = roleName == "Chair Person"
        ? "PendingChair"
        : "PendingDean";

    var announcement = await _context.Announcements
        .Include(a => a.Author)
            .ThenInclude(u => u.Role)
        .Include(a => a.Category)
        .Include(a => a.AnnouncementTags)
            .ThenInclude(at => at.DepartmentTag)
        .FirstOrDefaultAsync(a =>
            a.AnnouncementID == id &&
            a.ApprovalStatus == expectedStatus &&
            a.AnnouncementTags.Any(at =>
                at.TagID == primaryDept.TagID));

    if (announcement == null)
        return RedirectToAction("ReviewQueue");

    ViewBag.Role = roleName;
    return View(announcement);
}
```

- [ ] **Step 2: Create the Review view**

Create `EduConnect.Web/Views/Announcement/Review.cshtml`:

```cshtml
@model EduConnect.Web.Models.Announcement
@{
    ViewData["Title"] = "Review Announcement";
    string role = ViewBag.Role;
}

<div class="mb-4">
    <a href="/Announcement/ReviewQueue" class="btn btn-sm btn-outline-secondary">
        <i class="bi bi-arrow-left me-1"></i>Back to Queue
    </a>
</div>

@if (TempData["Error"] != null)
{
    <div class="alert alert-danger alert-dismissible fade show">
        @TempData["Error"]
        <button type="button" class="btn-close" data-bs-dismiss="alert"></button>
    </div>
}

<div class="row g-4">
    <!-- Announcement preview -->
    <div class="col-lg-8">
        <div class="card shadow-sm">
            <div class="card-header d-flex justify-content-between align-items-center">
                <span class="badge bg-primary">@Model.FeedType</span>
                <span class="badge bg-secondary">@Model.Category.CategoryName</span>
            </div>
            <div class="card-body">
                <h3 class="fw-bold">@Model.Title</h3>
                <div class="text-muted small mb-3">
                    By @Model.Author.FirstName @Model.Author.LastName
                    (@Model.Author.Role.RoleName) ·
                    Submitted @Model.SubmittedAt?.ToString("MMM dd, yyyy HH:mm")
                </div>
                <hr />
                <div>@Html.Raw(Model.Body)</div>
                @if (!string.IsNullOrEmpty(Model.AttachmentURL))
                {
                    <div class="mt-3">
                        <img src="@Model.AttachmentURL" class="img-fluid rounded" style="max-height:300px;" />
                    </div>
                }
                <div class="mt-3">
                    @foreach (var tag in Model.AnnouncementTags)
                    {
                        <span class="badge bg-light text-dark border me-1">
                            @tag.DepartmentTag.ShortName
                        </span>
                    }
                </div>
            </div>
        </div>
    </div>

    <!-- Decision panel -->
    <div class="col-lg-4">
        <div class="card shadow-sm border-success mb-3">
            <div class="card-body">
                <h6 class="fw-bold text-success mb-3">
                    <i class="bi bi-check-circle me-2"></i>Approve
                </h6>
                <form method="post" action="/Announcement/Approve/@Model.AnnouncementID">
                    @Html.AntiForgeryToken()
                    <button type="submit" class="btn btn-success w-100">
                        <i class="bi bi-check2 me-2"></i>
                        @(role == "Chair Person" ? "Approve & Forward to Dean" : "Approve & Notify Faculty")
                    </button>
                </form>
            </div>
        </div>

        <div class="card shadow-sm border-danger">
            <div class="card-body">
                <h6 class="fw-bold text-danger mb-3">
                    <i class="bi bi-x-circle me-2"></i>Reject
                </h6>
                <form method="post" action="/Announcement/Reject/@Model.AnnouncementID">
                    @Html.AntiForgeryToken()
                    <div class="mb-3">
                        <label class="form-label small fw-semibold">
                            Reason for rejection <span class="text-danger">*</span>
                        </label>
                        <textarea name="rejectionReason"
                                  class="form-control"
                                  rows="4"
                                  placeholder="Provide a reason for rejection"
                                  required></textarea>
                    </div>
                    <button type="submit" class="btn btn-danger w-100">
                        <i class="bi bi-x me-2"></i>Reject
                    </button>
                </form>
            </div>
        </div>
    </div>
</div>
```

- [ ] **Step 3: Verify manually**

Log in as Chair Person, go to `/Announcement/ReviewQueue`, click "Review" on a pending item. Should see the full announcement and both the Approve and Reject panels.

- [ ] **Step 4: Commit**

```bash
git add EduConnect.Web/Controllers/AnnouncementController.cs EduConnect.Web/Views/Announcement/Review.cshtml
git commit -m "feat: add Review action and view for Chair Person and Dean approval decisions"
```

---

## Task 8: Add Approve Action

**Files:**
- Modify: `EduConnect.Web/Controllers/AnnouncementController.cs`

- [ ] **Step 1: Add the Approve POST action**

```csharp
// ═══════════════════════════════════════
//  POST: /Announcement/Approve/{id}
//  Chair Person or Dean approves
// ═══════════════════════════════════════
[HttpPost]
[ValidateAntiForgeryToken]
public async Task<IActionResult> Approve(int id)
{
    if (!IsLoggedIn())
        return RedirectToAction("Login", "Account");

    var roleName = GetRoleName();
    if (roleName != "Chair Person" && roleName != "Dean")
        return RedirectToAction("Index");

    var userID = GetUserID();

    var primaryDept = await _context.UserDepartments
        .FirstOrDefaultAsync(ud =>
            ud.UserID == userID && ud.IsPrimary);

    if (primaryDept == null)
        return RedirectToAction("ReviewQueue");

    var expectedStatus = roleName == "Chair Person"
        ? "PendingChair"
        : "PendingDean";

    var announcement = await _context.Announcements
        .Include(a => a.AnnouncementTags)
        .FirstOrDefaultAsync(a =>
            a.AnnouncementID == id &&
            a.ApprovalStatus == expectedStatus &&
            a.AnnouncementTags.Any(at =>
                at.TagID == primaryDept.TagID));

    if (announcement == null)
        return RedirectToAction("ReviewQueue");

    if (roleName == "Chair Person")
    {
        announcement.ApprovalStatus = "PendingDean";
        announcement.ChairApprovedByID = userID;
        announcement.ChairApprovedAt = DateTime.Now;
        await _context.SaveChangesAsync();

        // Find Dean in same department
        var dean = await _context.UserDepartments
            .Include(ud => ud.User)
                .ThenInclude(u => u.Role)
            .Where(ud =>
                ud.TagID == primaryDept.TagID &&
                ud.User.Role.RoleName == "Dean" &&
                ud.User.IsActive)
            .Select(ud => ud.User)
            .FirstOrDefaultAsync();

        if (dean != null)
        {
            _ = _notificationService.SendAsync(
                dean.UserID,
                "AnnouncementReview",
                $"Announcement forwarded for your review: {announcement.Title}",
                $"/Announcement/Review/{announcement.AnnouncementID}",
                announcement.AnnouncementID);

            _ = _emailService.SendEmailAsync(
                dean.Email,
                $"{dean.FirstName} {dean.LastName}",
                "EduConnect: Announcement Pending Your Approval",
                $"<p>Hello {dean.FirstName},</p>" +
                $"<p>An announcement approved by the Chair Person now requires " +
                $"your review: <strong>{announcement.Title}</strong></p>" +
                $"<p><a href='https://localhost:7135/Announcement/Review/" +
                $"{announcement.AnnouncementID}'>Click here to review</a></p>");
        }

        TempData["Success"] =
            "Announcement approved and forwarded to the Dean.";
    }
    else // Dean
    {
        announcement.ApprovalStatus = "Approved";
        announcement.ApprovedByID = userID;
        announcement.ApprovedAt = DateTime.Now;
        await _context.SaveChangesAsync();

        var author = await _context.Users
            .FindAsync(announcement.AuthorID);

        _ = _notificationService.SendAsync(
            announcement.AuthorID,
            "AnnouncementApproved",
            $"Your announcement has been approved — you can now publish it",
            "/Announcement/MyAnnouncements",
            announcement.AnnouncementID);

        if (author != null)
        {
            _ = _emailService.SendEmailAsync(
                author.Email,
                $"{author.FirstName} {author.LastName}",
                "EduConnect: Announcement Approved",
                $"<p>Hello {author.FirstName},</p>" +
                $"<p>Your announcement <strong>{announcement.Title}</strong> " +
                $"has been approved by the Dean. You can now publish it.</p>" +
                $"<p><a href='https://localhost:7135/Announcement/MyAnnouncements'>" +
                $"Go to My Announcements</a></p>");
        }

        TempData["Success"] =
            "Announcement approved. Faculty has been notified.";
    }

    return RedirectToAction("ReviewQueue");
}
```

- [ ] **Step 2: Build**

```bash
dotnet build EduConnect.Web
```

Expected: `Build succeeded.`

- [ ] **Step 3: Verify manually**

Go through the full flow: Faculty submits → Chair Person reviews → clicks Approve. Announcement moves to `PendingDean`. Dean reviews → clicks Approve. Announcement moves to `Approved`. Faculty sees "Publish" button on My Announcements.

- [ ] **Step 4: Commit**

```bash
git add EduConnect.Web/Controllers/AnnouncementController.cs
git commit -m "feat: add Approve action for Chair Person and Dean with notifications"
```

---

## Task 9: Add Reject Action

**Files:**
- Modify: `EduConnect.Web/Controllers/AnnouncementController.cs`

- [ ] **Step 1: Add the Reject POST action**

```csharp
// ═══════════════════════════════════════
//  POST: /Announcement/Reject/{id}
//  Chair Person or Dean rejects
// ═══════════════════════════════════════
[HttpPost]
[ValidateAntiForgeryToken]
public async Task<IActionResult> Reject(int id, string rejectionReason)
{
    if (!IsLoggedIn())
        return RedirectToAction("Login", "Account");

    var roleName = GetRoleName();
    if (roleName != "Chair Person" && roleName != "Dean")
        return RedirectToAction("Index");

    if (string.IsNullOrWhiteSpace(rejectionReason))
    {
        TempData["Error"] =
            "A rejection reason is required.";
        return RedirectToAction("Review", new { id });
    }

    var userID = GetUserID();

    var primaryDept = await _context.UserDepartments
        .FirstOrDefaultAsync(ud =>
            ud.UserID == userID && ud.IsPrimary);

    if (primaryDept == null)
        return RedirectToAction("ReviewQueue");

    var expectedStatus = roleName == "Chair Person"
        ? "PendingChair"
        : "PendingDean";

    var announcement = await _context.Announcements
        .Include(a => a.AnnouncementTags)
        .FirstOrDefaultAsync(a =>
            a.AnnouncementID == id &&
            a.ApprovalStatus == expectedStatus &&
            a.AnnouncementTags.Any(at =>
                at.TagID == primaryDept.TagID));

    if (announcement == null)
        return RedirectToAction("ReviewQueue");

    announcement.ApprovalStatus = "Rejected";

    if (roleName == "Chair Person")
        announcement.ChairRejectionReason = rejectionReason;
    else
        announcement.RejectionReason = rejectionReason;

    await _context.SaveChangesAsync();

    var author = await _context.Users
        .FindAsync(announcement.AuthorID);
    var rejectedBy = roleName == "Chair Person"
        ? "the Chair Person"
        : "the Dean";

    _ = _notificationService.SendAsync(
        announcement.AuthorID,
        "AnnouncementRejected",
        $"Your announcement was rejected by {rejectedBy}",
        "/Announcement/MyAnnouncements",
        announcement.AnnouncementID);

    if (author != null)
    {
        _ = _emailService.SendEmailAsync(
            author.Email,
            $"{author.FirstName} {author.LastName}",
            "EduConnect: Announcement Rejected",
            $"<p>Hello {author.FirstName},</p>" +
            $"<p>Your announcement <strong>{announcement.Title}</strong> " +
            $"was rejected by {rejectedBy}.</p>" +
            $"<p><strong>Reason:</strong> {rejectionReason}</p>" +
            $"<p><a href='https://localhost:7135/Announcement/MyAnnouncements'>" +
            $"Go to My Announcements to revise and resubmit</a></p>");
    }

    TempData["Success"] = "Announcement rejected. Faculty has been notified.";
    return RedirectToAction("ReviewQueue");
}
```

- [ ] **Step 2: Build**

```bash
dotnet build EduConnect.Web
```

Expected: `Build succeeded.`

- [ ] **Step 3: Verify manually**

On the Review page, leave the rejection reason blank and hit Reject — should redirect back to Review with the error message visible. Fill in a reason and reject — announcement returns to Rejected state. Faculty sees the rejection reason on My Announcements.

- [ ] **Step 4: Commit**

```bash
git add EduConnect.Web/Controllers/AnnouncementController.cs
git commit -m "feat: add Reject action for Chair Person and Dean with reason + notification"
```

---

## Task 10: Add Publish Action

**Files:**
- Modify: `EduConnect.Web/Controllers/AnnouncementController.cs`

- [ ] **Step 1: Add the Publish POST action**

```csharp
// ═══════════════════════════════════════
//  POST: /Announcement/Publish/{id}
//  Faculty self-publishes after approval
// ═══════════════════════════════════════
[HttpPost]
[ValidateAntiForgeryToken]
public async Task<IActionResult> Publish(int id)
{
    if (!IsLoggedIn())
        return RedirectToAction("Login", "Account");

    if (!IsFaculty())
        return RedirectToAction("Index");

    var userID = GetUserID();

    var announcement = await _context.Announcements
        .Include(a => a.AnnouncementTags)
            .ThenInclude(at => at.DepartmentTag)
        .FirstOrDefaultAsync(a =>
            a.AnnouncementID == id &&
            a.AuthorID == userID &&
            a.ApprovalStatus == "Approved" &&
            a.Status != "Published");

    if (announcement == null)
        return RedirectToAction("MyAnnouncements");

    announcement.Status = "Published";
    announcement.PublishedAt = DateTime.Now;
    await _context.SaveChangesAsync();

    // Notify department members
    var tagIDs = announcement.AnnouncementTags
        .Select(at => at.TagID)
        .ToList();

    if (tagIDs.Any())
    {
        bool broadcastAll = announcement.AnnouncementTags
            .Any(at =>
                at.DepartmentTag.ShortName == "ALL" ||
                at.DepartmentTag.ShortName == "Emergency");

        List<int> recipientIds;
        if (broadcastAll)
        {
            recipientIds = await _context.Users
                .Where(u => u.IsActive && u.UserID != userID)
                .Select(u => u.UserID)
                .ToListAsync();
        }
        else
        {
            recipientIds = await _context.UserDepartments
                .Where(ud => tagIDs.Contains(ud.TagID))
                .Select(ud => ud.UserID)
                .Distinct()
                .Where(uid => uid != userID)
                .ToListAsync();
        }

        if (recipientIds.Count > 0)
        {
            await _notificationService.SendToManyAsync(
                recipientIds,
                "Announcement",
                $"New announcement: {announcement.Title}",
                $"/Announcement/Details/{announcement.AnnouncementID}",
                announcement.AnnouncementID);
        }
    }

    TempData["Success"] =
        "Announcement published successfully!";
    return RedirectToAction("MyAnnouncements");
}
```

- [ ] **Step 2: Build**

```bash
dotnet build EduConnect.Web
```

Expected: `Build succeeded.`

- [ ] **Step 3: Verify manually**

After Dean approves, log in as Faculty. My Announcements shows "Publish" button. Click it — announcement becomes live and visible in `/Announcement`. Department members receive a real-time notification.

- [ ] **Step 4: Commit**

```bash
git add EduConnect.Web/Controllers/AnnouncementController.cs
git commit -m "feat: add Publish action so Faculty can self-publish approved announcements"
```

---

## Task 11: Update Edit Action — Block Faculty During Review

**Files:**
- Modify: `EduConnect.Web/Controllers/AnnouncementController.cs`

- [ ] **Step 1: Update Edit GET to block Faculty if announcement is in review**

In the `GET /Announcement/Edit` action, after the `if (!CanEditAnnouncement(announcement))` check, add:

```csharp
if (IsFaculty() &&
    announcement.ApprovalStatus != "Draft" &&
    announcement.ApprovalStatus != "Rejected")
{
    TempData["Error"] =
        "This announcement cannot be edited while under review.";
    return RedirectToAction("MyAnnouncements");
}
```

- [ ] **Step 2: Update Edit POST to reset ApprovalStatus when Faculty edits a Rejected announcement**

In the `POST /Announcement/Edit` action, after the ownership check (`if (!CanEditAnnouncement(announcement))`), add:

```csharp
if (IsFaculty() &&
    announcement.ApprovalStatus != "Draft" &&
    announcement.ApprovalStatus != "Rejected")
{
    TempData["Error"] =
        "This announcement cannot be edited while under review.";
    return RedirectToAction("MyAnnouncements");
}
```

Then, just before `announcement.Title = model.Title;`, add:

```csharp
// Reset to Draft if Faculty edits a rejected announcement
if (IsFaculty() && announcement.ApprovalStatus == "Rejected")
{
    announcement.ApprovalStatus = "Draft";
    announcement.ChairRejectionReason = null;
    announcement.RejectionReason = null;
}
```

Also update the Edit POST's final redirect for Faculty:

Replace:
```csharp
TempData["Success"] =
    "Announcement updated successfully!";
return RedirectToAction("Index");
```
with:
```csharp
TempData["Success"] =
    "Announcement updated successfully!";

if (IsFaculty())
    return RedirectToAction("MyAnnouncements");

return RedirectToAction("Index");
```

- [ ] **Step 3: Build**

```bash
dotnet build EduConnect.Web
```

Expected: `Build succeeded.`

- [ ] **Step 4: Verify manually**

With an announcement in `PendingChair`, try navigating to `/Announcement/Edit/{id}` as the Faculty author. Should be redirected to My Announcements with an error. With a `Rejected` announcement, edit it — after save, status should be back to `Draft` and rejection reasons cleared.

- [ ] **Step 5: Commit**

```bash
git add EduConnect.Web/Controllers/AnnouncementController.cs
git commit -m "feat: prevent Faculty from editing in-review announcements; reset Draft on edit after rejection"
```

---

## Task 12: Nav Links — Layout Updates

**Files:**
- Modify: `EduConnect.Web/Views/Shared/_Layout.cshtml`

- [ ] **Step 1: Add "My Announcements" link to Faculty sidebar**

In `_Layout.cshtml`, find the `else if (currentRole == "Faculty")` block. After the existing "New Announcement" `<li>` (around line 378–384), add:

```html
<li class="nav-item">
    <a href="/Announcement/MyAnnouncements"
       class="nav-link text-dark">
        <i class="bi bi-file-earmark-text me-2"></i>
        My Announcements
    </a>
</li>
```

- [ ] **Step 2: Add "Pending Reviews" link to Dean sidebar**

In the `else if (currentRole == "Dean")` block, add after the existing "Pending Approvals" `<li>`:

```html
<li class="nav-item">
    <a href="/Announcement/ReviewQueue"
       class="nav-link text-dark">
        <i class="bi bi-hourglass-split me-2"></i>
        Pending Reviews
    </a>
</li>
```

- [ ] **Step 3: Add a full Chair Person sidebar block**

Currently Chair Person falls through to the Student `else` block — it has no sidebar. Add a new `else if` block between the Faculty block and the Staff block:

```html
else if (currentRole == "Chair Person")
{
    <li class="nav-item">
        <a href="/Announcement/ReviewQueue"
           class="nav-link text-dark">
            <i class="bi bi-hourglass-split me-2"></i>
            Pending Reviews
        </a>
    </li>
    <li class="nav-item">
        <a href="/Announcement/Create"
           class="nav-link text-dark">
            <i class="bi bi-plus-circle me-2"></i>
            New Announcement
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
        <a href="/Group"
           class="nav-link text-dark">
            <i class="bi bi-people me-2"></i>
            Group Finder
        </a>
    </li>
    <li class="nav-item">
        <a href="/Event/Scanner"
           class="nav-link text-dark">
            <i class="bi bi-qr-code-scan me-2"></i>
            QR Scanner
        </a>
    </li>
    <li class="nav-item">
        <a href="/Notification"
           class="nav-link text-dark">
            <i class="bi bi-bell me-2"></i>
            Notifications
        </a>
    </li>
    <li class="nav-item">
        <a href="/SafetyReport/Submit"
           class="nav-link text-dark">
            <i class="bi bi-flag me-2"></i>
            Report Safety Issue
        </a>
    </li>
}
```

- [ ] **Step 4: Verify manually**

Log in as Faculty — sidebar should show "My Announcements" link. Log in as Chair Person — sidebar should show "Pending Reviews" and a proper nav (no longer showing Student nav). Log in as Dean — sidebar should show "Pending Reviews".

- [ ] **Step 5: Commit**

```bash
git add EduConnect.Web/Views/Shared/_Layout.cshtml
git commit -m "feat: add Chair Person sidebar and Pending Reviews / My Announcements nav links"
```

---

## End-to-End Smoke Test

After all tasks are complete, run this full flow manually:

1. Log in as **Faculty** → Create a new announcement → "Save Draft" button → verify redirected to My Announcements with Draft badge
2. Click **Submit for Review** → verify status changes to "Pending Chair" (or "Pending Dean")
3. Log in as **Chair Person** → Sidebar shows "Pending Reviews" → click it → see the submitted announcement → click Review → Approve → verify redirected to queue with success message
4. Log in as **Dean** → Sidebar shows "Pending Reviews" → see the forwarded announcement → click Review → Approve → verify Faculty notified
5. Log in as **Faculty** → My Announcements shows "Publish" button → click it → verify announcement appears in `/Announcement` feed
6. Repeat steps 1–2, then have Chair Person **reject** it → Faculty sees red "Rejected" badge with reason → Faculty edits it → status resets to Draft → can resubmit
