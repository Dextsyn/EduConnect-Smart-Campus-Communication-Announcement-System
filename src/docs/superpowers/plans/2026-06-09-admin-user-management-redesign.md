# Admin User Management Redesign — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Restrict the Administrator role to user management only — stripping all announcement/event write access — and add full user CRUD (add, edit full details, hard delete).

**Architecture:** Surgical edits across two controllers to remove `"Administrator"` from role-gating helpers; a full rewrite of the admin dashboard to user-centric stats; three new actions on `AdminController` (AddUser, EditUser, DeleteUser) with matching views; and sidebar/nav cleanup in `_Layout.cshtml`. No new services — all actions use `_context` directly, matching the existing pattern.

**Tech Stack:** ASP.NET Core 8 MVC, EF Core, SQL Server, BCrypt.Net-Next, Bootstrap 5, Bootstrap Icons, Razor views.

---

## File Map

| File | Action |
|------|--------|
| `Controllers/AnnouncementController.cs` | Modify `CanCreate()`, `CanEditAnnouncement()` |
| `Controllers/EventController.cs` | Modify `CanManageEvents()`, `CanScan()`, inline admin checks (~line 967, ~line 1094) |
| `Controllers/AdminController.cs` | Rewrite `Index()`; add `AddUser` GET+POST, `EditUser` GET+POST, `DeleteUser` POST; remove `ChangeUserRole` |
| `Views/Admin/Index.cshtml` | Full rewrite — user-centric dashboard |
| `Views/Admin/Users.cshtml` | Replace role dropdown + toggle button with Edit + Delete buttons; add delete modal |
| `Views/Admin/AddUser.cshtml` | New file |
| `Views/Admin/EditUser.cshtml` | New file |
| `Views/Shared/_Layout.cshtml` | Trim admin sidebar; remove admin from QR Scanner nav condition |
| `ViewModel/AdminUserFormViewModel.cs` | New file — shared by Add and Edit forms |

---

## Task 1: Strip Announcement Write Permissions from Admin

**Files:**
- Modify: `EduConnect.Web/Controllers/AnnouncementController.cs` (~lines 52–66)

- [ ] **Step 1: Remove `"Administrator"` from `CanCreate()`**

Find `CanCreate()` (around line 59) and change it to:

```csharp
private bool CanCreate()
{
    var role = GetRoleName();
    return role == "Dean" ||
           role == "Chair Person" ||
           role == "Faculty";
}
```

- [ ] **Step 2: Remove the admin bypass in `CanEditAnnouncement()`**

Find `CanEditAnnouncement()` (around line 52) and change it to:

```csharp
private bool CanEditAnnouncement(Announcement a) =>
    a.AuthorID == GetUserID();
```

- [ ] **Step 3: Build to confirm no compile errors**

```powershell
dotnet build EduConnect.Web
```
Expected: `Build succeeded` with 0 errors.

- [ ] **Step 4: Commit**

```powershell
git add EduConnect.Web/Controllers/AnnouncementController.cs
git commit -m "feat: remove admin from announcement create/edit permissions"
```

---

## Task 2: Strip Event Write Permissions from Admin

**Files:**
- Modify: `EduConnect.Web/Controllers/EventController.cs`

- [ ] **Step 1: Remove `"Administrator"` from `CanManageEvents()`**

Find `CanManageEvents()` (around line 47) and change it to:

```csharp
private bool CanManageEvents()
{
    var role = GetRoleName();
    return role == "Faculty" ||
           role == "Dean" ||
           role == "Chair Person";
}
```

- [ ] **Step 2: Remove `"Administrator"` from `CanScan()`**

Find `CanScan()` (around line 56) and change it to:

```csharp
private bool CanScan()
{
    var role = GetRoleName();
    return role == "Faculty" ||
           role == "Dean" ||
           role == "Chair Person";
}
```

- [ ] **Step 3: Remove inline admin organizer bypass (first occurrence)**

Find the block around line 965:
```csharp
bool canAccess =
    ev.OrganizerID == userID ||
    roleName == "Administrator";
```
Change to:
```csharp
bool canAccess =
    ev.OrganizerID == userID;
```

- [ ] **Step 4: Remove inline admin organizer bypass (second occurrence)**

Find the second identical pattern (around line 1092–1094) with the same `roleName == "Administrator"` guard and apply the same removal:
```csharp
bool canAccess =
    registration.Event.OrganizerID == userID;
```

- [ ] **Step 5: Build**

```powershell
dotnet build EduConnect.Web
```
Expected: `Build succeeded` with 0 errors.

- [ ] **Step 6: Commit**

```powershell
git add EduConnect.Web/Controllers/EventController.cs
git commit -m "feat: remove admin from event manage/scan permissions"
```

---

## Task 3: Create AdminUserFormViewModel

**Files:**
- Create: `EduConnect.Web/ViewModel/AdminUserFormViewModel.cs`

- [ ] **Step 1: Create the ViewModel**

```csharp
using System.ComponentModel.DataAnnotations;
using EduConnect.Web.Models;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace EduConnect.Web.ViewModels
{
    public class AdminUserFormViewModel
    {
        public int UserID { get; set; }

        [Required(ErrorMessage = "First name is required")]
        [MaxLength(50)]
        public string FirstName { get; set; }

        [Required(ErrorMessage = "Last name is required")]
        [MaxLength(50)]
        public string LastName { get; set; }

        [Required(ErrorMessage = "Email is required")]
        [EmailAddress(ErrorMessage = "Invalid email format")]
        [MaxLength(100)]
        public string Email { get; set; }

        // Leave blank on edit to keep existing password
        [DataType(DataType.Password)]
        [MinLength(6, ErrorMessage = "Password must be at least 6 characters")]
        public string? Password { get; set; }

        [MaxLength(50)]
        public string? StudentID { get; set; }

        [Required(ErrorMessage = "Role is required")]
        public int RoleID { get; set; }

        [Required(ErrorMessage = "Department is required")]
        public int DepartmentTagID { get; set; }

        public bool IsActive { get; set; } = true;

        // Populated by the controller for the dropdowns
        public List<SelectListItem> Roles { get; set; } = new();
        public List<SelectListItem> Departments { get; set; } = new();
    }
}
```

- [ ] **Step 2: Build**

```powershell
dotnet build EduConnect.Web
```
Expected: `Build succeeded`.

- [ ] **Step 3: Commit**

```powershell
git add EduConnect.Web/ViewModel/AdminUserFormViewModel.cs
git commit -m "feat: add AdminUserFormViewModel for add/edit user forms"
```

---

## Task 4: Rewrite AdminController.Index() with User-Centric Stats

**Files:**
- Modify: `EduConnect.Web/Controllers/AdminController.cs` — replace the `Index()` action body

- [ ] **Step 1: Replace the `Index()` action**

Replace the entire `Index()` method body (everything inside the action, keeping the method signature) with:

```csharp
public async Task<IActionResult> Index()
{
    if (!IsAdmin())
        return RedirectToAction("Login", "Account");

    // ─── Stat Cards ────────────────────────
    ViewBag.TotalUsers = await _context.Users
        .Where(u => u.VerificationStatus == "Verified")
        .CountAsync();

    ViewBag.PendingVerifications = await _context.Users
        .Where(u => u.VerificationStatus == "Pending")
        .CountAsync();

    ViewBag.CountFaculty = await _context.Users
        .Where(u => u.Role.RoleName == "Faculty" && u.IsActive)
        .CountAsync();

    ViewBag.CountDean = await _context.Users
        .Where(u => u.Role.RoleName == "Dean" && u.IsActive)
        .CountAsync();

    ViewBag.CountChairPerson = await _context.Users
        .Where(u => u.Role.RoleName == "Chair Person" && u.IsActive)
        .CountAsync();

    ViewBag.CountStaff = await _context.Users
        .Where(u => u.Role.RoleName == "Staff" && u.IsActive)
        .CountAsync();

    ViewBag.CountStudent = await _context.Users
        .Where(u => u.Role.RoleName == "Student" && u.IsActive)
        .CountAsync();

    // ─── Chart: New Registrations Last 6 Months ──
    var months = Enumerable.Range(0, 6)
        .Select(i => DateTime.Now.AddMonths(-i))
        .Reverse()
        .ToList();

    ViewBag.MonthLabels = months
        .Select(m => m.ToString("MMM yyyy"))
        .ToList();

    ViewBag.MonthlyRegistrations = months
        .Select(m => _context.Users
            .Count(u =>
                u.CreatedAt.Month == m.Month &&
                u.CreatedAt.Year == m.Year))
        .ToList();

    // ─── Chart: Users by Role ───────────────
    var roleData = await _context.Users
        .Where(u => u.IsActive)
        .GroupBy(u => u.Role.RoleName)
        .Select(g => new { Role = g.Key, Count = g.Count() })
        .ToListAsync();

    ViewBag.RoleLabels = roleData.Select(r => r.Role).ToList();
    ViewBag.RoleCount = roleData.Select(r => r.Count).ToList();

    // ─── Recent Pending Verifications ───────
    ViewBag.RecentPendingUsers = await _context.Users
        .Include(u => u.UserDepartments)
            .ThenInclude(ud => ud.DepartmentTag)
        .Where(u => u.VerificationStatus == "Pending")
        .OrderBy(u => u.CreatedAt)
        .Take(5)
        .ToListAsync();

    // ─── Recently Added Users ───────────────
    ViewBag.RecentlyAddedUsers = await _context.Users
        .Include(u => u.Role)
        .Include(u => u.UserDepartments)
            .ThenInclude(ud => ud.DepartmentTag)
        .Where(u => u.VerificationStatus == "Verified")
        .OrderByDescending(u => u.VerifiedAt)
        .Take(5)
        .ToListAsync();

    return View();
}
```

- [ ] **Step 2: Build**

```powershell
dotnet build EduConnect.Web
```
Expected: `Build succeeded`.

- [ ] **Step 3: Commit**

```powershell
git add EduConnect.Web/Controllers/AdminController.cs
git commit -m "feat: rewrite admin dashboard with user-centric stats"
```

---

## Task 5: Rewrite Admin/Index.cshtml — User-Centric Dashboard

**Files:**
- Modify: `EduConnect.Web/Views/Admin/Index.cshtml` — full replacement

- [ ] **Step 1: Replace the entire file content**

```razor
@{
    ViewData["Title"] = "Admin Dashboard";
    var userName = Context.Session.GetString("UserName");
    var monthLabels = ViewBag.MonthLabels as List<string> ?? new List<string>();
    var monthlyRegs = ViewBag.MonthlyRegistrations as List<int> ?? new List<int>();
    var roleLabels = ViewBag.RoleLabels as List<string> ?? new List<string>();
    var roleCount = ViewBag.RoleCount as List<int> ?? new List<int>();
    var pendingUsers = ViewBag.RecentPendingUsers as List<EduConnect.Web.Models.User>
        ?? new List<EduConnect.Web.Models.User>();
    var recentUsers = ViewBag.RecentlyAddedUsers as List<EduConnect.Web.Models.User>
        ?? new List<EduConnect.Web.Models.User>();
}

<!-- ─── PAGE HEADER ──────────────────── -->
<div class="d-flex justify-content-between
            align-items-center mb-4 flex-wrap gap-2">
    <div>
        <h4 class="fw-bold mb-1">
            <i class="bi bi-speedometer2 me-2 text-primary"></i>
            Admin Dashboard
        </h4>
        <small class="text-muted">Welcome back, @userName</small>
    </div>
    <div class="d-flex gap-2 flex-wrap">
        <a href="/Admin/PendingUsers" class="btn btn-warning btn-sm">
            <i class="bi bi-person-check me-1"></i>
            Pending
            @if (ViewBag.PendingVerifications > 0)
            {
                <span class="badge bg-dark ms-1">@ViewBag.PendingVerifications</span>
            }
        </a>
        <a href="/Admin/AddUser" class="btn btn-primary btn-sm">
            <i class="bi bi-person-plus me-1"></i>
            Add User
        </a>
    </div>
</div>

<!-- ─── STAT CARDS ───────────────────── -->
<div class="row g-3 mb-4">

    <div class="col-6 col-md-2">
        <div class="card border-0 shadow-sm h-100">
            <div class="card-body text-center p-3">
                <div class="rounded-circle p-2 bg-primary bg-opacity-10 d-inline-flex mb-2">
                    <i class="bi bi-people text-primary fs-5"></i>
                </div>
                <div class="fw-bold fs-4">@ViewBag.TotalUsers</div>
                <div class="text-muted small">Verified Users</div>
            </div>
        </div>
    </div>

    <div class="col-6 col-md-2">
        <div class="card border-0 shadow-sm h-100
                    @(ViewBag.PendingVerifications > 0 ? "border-warning border-2" : "")">
            <div class="card-body text-center p-3">
                <div class="rounded-circle p-2 bg-warning bg-opacity-10 d-inline-flex mb-2">
                    <i class="bi bi-person-check text-warning fs-5"></i>
                </div>
                <div class="fw-bold fs-4">@ViewBag.PendingVerifications</div>
                <div class="text-muted small">Pending</div>
            </div>
        </div>
    </div>

    <div class="col-6 col-md-2">
        <div class="card border-0 shadow-sm h-100">
            <div class="card-body text-center p-3">
                <div class="rounded-circle p-2 bg-success bg-opacity-10 d-inline-flex mb-2">
                    <i class="bi bi-mortarboard text-success fs-5"></i>
                </div>
                <div class="fw-bold fs-4">@ViewBag.CountStudent</div>
                <div class="text-muted small">Students</div>
            </div>
        </div>
    </div>

    <div class="col-6 col-md-2">
        <div class="card border-0 shadow-sm h-100">
            <div class="card-body text-center p-3">
                <div class="rounded-circle p-2 bg-info bg-opacity-10 d-inline-flex mb-2">
                    <i class="bi bi-person-badge text-info fs-5"></i>
                </div>
                <div class="fw-bold fs-4">@ViewBag.CountFaculty</div>
                <div class="text-muted small">Faculty</div>
            </div>
        </div>
    </div>

    <div class="col-6 col-md-2">
        <div class="card border-0 shadow-sm h-100">
            <div class="card-body text-center p-3">
                <div class="rounded-circle p-2 bg-purple bg-opacity-10 d-inline-flex mb-2">
                    <i class="bi bi-briefcase text-secondary fs-5"></i>
                </div>
                <div class="fw-bold fs-4">@ViewBag.CountStaff</div>
                <div class="text-muted small">Staff</div>
            </div>
        </div>
    </div>

    <div class="col-6 col-md-2">
        <div class="card border-0 shadow-sm h-100">
            <div class="card-body text-center p-3">
                <div class="rounded-circle p-2 bg-secondary bg-opacity-10 d-inline-flex mb-2">
                    <i class="bi bi-building text-secondary fs-5"></i>
                </div>
                <div class="fw-bold fs-4">@(ViewBag.CountDean + ViewBag.CountChairPerson)</div>
                <div class="text-muted small">Dean / Chair</div>
            </div>
        </div>
    </div>

</div>

<!-- ─── PENDING USERS ALERT ───────────── -->
@if (pendingUsers.Any())
{
    <div class="card border-0 shadow-sm mb-4 border-start border-warning border-4">
        <div class="card-header bg-white border-0 pt-3
                    d-flex justify-content-between align-items-center">
            <h6 class="fw-bold mb-0 text-warning">
                <i class="bi bi-exclamation-circle me-2"></i>
                Pending Student Verifications
                <span class="badge bg-warning text-dark ms-1">
                    @ViewBag.PendingVerifications
                </span>
            </h6>
            <a href="/Admin/PendingUsers" class="btn btn-warning btn-sm">
                View All <i class="bi bi-arrow-right ms-1"></i>
            </a>
        </div>
        <div class="card-body pt-0">
            <div class="row g-2">
                @foreach (var user in pendingUsers)
                {
                    var dept = user.UserDepartments.FirstOrDefault(ud => ud.IsPrimary);
                    <div class="col-12 col-md-6">
                        <div class="d-flex align-items-center gap-2 p-2 bg-light rounded">
                            <img src="https://ui-avatars.com/api/?name=@user.FirstName+@user.LastName&background=ffc107&color=000&size=32"
                                 class="rounded-circle" width="32" height="32" />
                            <div class="flex-grow-1 small">
                                <div class="fw-semibold">@user.FirstName @user.LastName</div>
                                <div class="text-muted">
                                    @(user.StudentID ?? "No ID") •
                                    @(dept?.DepartmentTag?.ShortName ?? "—")
                                </div>
                            </div>
                            <span class="badge bg-warning text-dark">Pending</span>
                        </div>
                    </div>
                }
            </div>
        </div>
    </div>
}

<!-- ─── CHARTS ROW ────────────────────── -->
<div class="row g-3 mb-4">

    <div class="col-12 col-lg-8">
        <div class="card border-0 shadow-sm">
            <div class="card-header bg-white border-0 pt-3">
                <h6 class="fw-bold mb-0">
                    <i class="bi bi-graph-up me-2 text-primary"></i>
                    New Registrations — Last 6 Months
                </h6>
            </div>
            <div class="card-body">
                <canvas id="registrationsChart" height="100"></canvas>
            </div>
        </div>
    </div>

    <div class="col-12 col-lg-4">
        <div class="card border-0 shadow-sm">
            <div class="card-header bg-white border-0 pt-3">
                <h6 class="fw-bold mb-0">
                    <i class="bi bi-pie-chart me-2 text-primary"></i>
                    Users by Role
                </h6>
            </div>
            <div class="card-body">
                <canvas id="roleChart" height="200"></canvas>
            </div>
        </div>
    </div>

</div>

<!-- ─── QUICK ACTIONS ─────────────────── -->
<div class="card border-0 shadow-sm mb-4">
    <div class="card-header bg-white border-0 pt-3">
        <h6 class="fw-bold mb-0">
            <i class="bi bi-lightning me-2 text-primary"></i>
            Quick Actions
        </h6>
    </div>
    <div class="card-body">
        <div class="row g-3">
            <div class="col-6 col-md-4">
                <a href="/Admin/PendingUsers"
                   class="btn btn-outline-warning w-100 py-3 text-center">
                    <i class="bi bi-person-check d-block fs-3 mb-1"></i>
                    <small>Verify Students</small>
                </a>
            </div>
            <div class="col-6 col-md-4">
                <a href="/Admin/Users"
                   class="btn btn-outline-primary w-100 py-3 text-center">
                    <i class="bi bi-people d-block fs-3 mb-1"></i>
                    <small>Manage Users</small>
                </a>
            </div>
            <div class="col-6 col-md-4">
                <a href="/Admin/AddUser"
                   class="btn btn-outline-success w-100 py-3 text-center">
                    <i class="bi bi-person-plus d-block fs-3 mb-1"></i>
                    <small>Add User</small>
                </a>
            </div>
        </div>
    </div>
</div>

<!-- ─── RECENTLY ADDED USERS TABLE ───── -->
<div class="card border-0 shadow-sm">
    <div class="card-header bg-white border-0 pt-3
                d-flex justify-content-between align-items-center">
        <h6 class="fw-bold mb-0">
            <i class="bi bi-person-plus me-2 text-primary"></i>
            Recently Added Users
        </h6>
        <a href="/Admin/Users" class="btn btn-outline-primary btn-sm">
            View All
        </a>
    </div>
    <div class="card-body p-0">
        <div class="table-responsive">
            <table class="table table-hover mb-0">
                <thead class="table-light">
                    <tr>
                        <th class="ps-3">User</th>
                        <th>Role</th>
                        <th>Department</th>
                        <th>Status</th>
                        <th>Joined</th>
                    </tr>
                </thead>
                <tbody>
                    @if (!recentUsers.Any())
                    {
                        <tr>
                            <td colspan="5" class="text-center py-4 text-muted">
                                No users yet
                            </td>
                        </tr>
                    }
                    else
                    {
                        @foreach (var user in recentUsers)
                        {
                            var dept = user.UserDepartments
                                .FirstOrDefault(ud => ud.IsPrimary);
                            <tr>
                                <td class="ps-3">
                                    <div class="d-flex align-items-center gap-2">
                                        <img src="https://ui-avatars.com/api/?name=@user.FirstName+@user.LastName&background=0d6efd&color=fff&size=32"
                                             class="rounded-circle" width="32" height="32" />
                                        <div>
                                            <div class="fw-semibold small">
                                                @user.FirstName @user.LastName
                                            </div>
                                            <div class="text-muted" style="font-size:11px">
                                                @user.Email
                                            </div>
                                        </div>
                                    </div>
                                </td>
                                <td>
                                    <span class="badge bg-primary bg-opacity-10
                                                 text-primary border border-primary
                                                 border-opacity-25 small">
                                        @user.Role.RoleName
                                    </span>
                                </td>
                                <td class="small">
                                    @(dept?.DepartmentTag?.ShortName ?? "—")
                                </td>
                                <td>
                                    @if (user.IsActive)
                                    {
                                        <span class="badge bg-success">Active</span>
                                    }
                                    else
                                    {
                                        <span class="badge bg-secondary">Inactive</span>
                                    }
                                </td>
                                <td class="text-muted small">
                                    @user.CreatedAt.ToString("MMM dd, yyyy")
                                </td>
                            </tr>
                        }
                    }
                </tbody>
            </table>
        </div>
    </div>
</div>

@section Scripts {
<script>
    new Chart(document.getElementById('registrationsChart').getContext('2d'), {
        type: 'bar',
        data: {
            labels: @Html.Raw(System.Text.Json.JsonSerializer.Serialize(monthLabels)),
            datasets: [{
                label: 'New Users',
                data: @Html.Raw(System.Text.Json.JsonSerializer.Serialize(monthlyRegs)),
                backgroundColor: 'rgba(13,110,253,0.7)',
                borderRadius: 4
            }]
        },
        options: {
            responsive: true,
            plugins: { legend: { display: false } },
            scales: { y: { beginAtZero: true, ticks: { stepSize: 1 } } }
        }
    });

    new Chart(document.getElementById('roleChart').getContext('2d'), {
        type: 'doughnut',
        data: {
            labels: @Html.Raw(System.Text.Json.JsonSerializer.Serialize(roleLabels)),
            datasets: [{
                data: @Html.Raw(System.Text.Json.JsonSerializer.Serialize(roleCount)),
                backgroundColor: ['#0d6efd','#198754','#dc3545','#ffc107','#0dcaf0','#6f42c1'],
                borderWidth: 2,
                borderColor: '#fff'
            }]
        },
        options: {
            responsive: true,
            plugins: { legend: { position: 'bottom', labels: { font: { size: 11 } } } }
        }
    });
</script>
}
```

- [ ] **Step 2: Build**

```powershell
dotnet build EduConnect.Web
```
Expected: `Build succeeded`.

- [ ] **Step 3: Commit**

```powershell
git add EduConnect.Web/Views/Admin/Index.cshtml
git commit -m "feat: redesign admin dashboard with user-centric stats and charts"
```

---

## Task 6: Add AddUser Action to AdminController

**Files:**
- Modify: `EduConnect.Web/Controllers/AdminController.cs`

Add the following `using` at the top of the file if not already present:
```csharp
using EduConnect.Web.ViewModels;
using BCrypt.Net;
using Microsoft.AspNetCore.Mvc.Rendering;
```

- [ ] **Step 1: Add `GET /Admin/AddUser` action**

Add this method to `AdminController` before the closing `}` of the class:

```csharp
// ═══════════════════════════════════════
//  GET: /Admin/AddUser
// ═══════════════════════════════════════
public async Task<IActionResult> AddUser()
{
    if (!IsAdmin())
        return RedirectToAction("Login", "Account");

    var model = new AdminUserFormViewModel
    {
        Roles = (await _context.Roles.ToListAsync())
            .Select(r => new SelectListItem(r.RoleName, r.RoleID.ToString()))
            .ToList(),
        Departments = (await _context.DepartmentTags
            .Where(d => d.IsActive)
            .ToListAsync())
            .Select(d => new SelectListItem(
                $"{d.ShortName} — {d.TagName}", d.TagID.ToString()))
            .ToList()
    };
    return View(model);
}
```

- [ ] **Step 2: Add `POST /Admin/AddUser` action**

```csharp
// ═══════════════════════════════════════
//  POST: /Admin/AddUser
// ═══════════════════════════════════════
[HttpPost]
[ValidateAntiForgeryToken]
public async Task<IActionResult> AddUser(AdminUserFormViewModel model)
{
    if (!IsAdmin())
        return RedirectToAction("Login", "Account");

    // Require password for new users
    if (string.IsNullOrWhiteSpace(model.Password))
        ModelState.AddModelError("Password", "Password is required when creating a user.");

    // Check email uniqueness
    if (await _context.Users.AnyAsync(u => u.Email == model.Email))
        ModelState.AddModelError("Email", "A user with this email already exists.");

    if (!ModelState.IsValid)
    {
        model.Roles = (await _context.Roles.ToListAsync())
            .Select(r => new SelectListItem(r.RoleName, r.RoleID.ToString()))
            .ToList();
        model.Departments = (await _context.DepartmentTags
            .Where(d => d.IsActive)
            .ToListAsync())
            .Select(d => new SelectListItem(
                $"{d.ShortName} — {d.TagName}", d.TagID.ToString()))
            .ToList();
        return View(model);
    }

    var adminID = int.Parse(HttpContext.Session.GetString("UserID"));

    var user = new User
    {
        FirstName = model.FirstName,
        LastName = model.LastName,
        Email = model.Email,
        PasswordHash = BCrypt.Net.BCrypt.HashPassword(model.Password),
        StudentID = model.StudentID,
        RoleID = model.RoleID,
        IsActive = model.IsActive,
        VerificationStatus = "Verified",
        VerifiedByID = adminID,
        VerifiedAt = DateTime.Now,
        CreatedAt = DateTime.Now
    };

    _context.Users.Add(user);
    await _context.SaveChangesAsync();

    _context.UserDepartments.Add(new UserDepartment
    {
        UserID = user.UserID,
        TagID = model.DepartmentTagID,
        IsPrimary = true,
        CreatedAt = DateTime.Now
    });
    await _context.SaveChangesAsync();

    // Send welcome email (fire-and-forget)
    try
    {
        var emailBody = $@"
        <div style='font-family:Arial,sans-serif;max-width:600px;margin:0 auto;'>
            <div style='background:#0d6efd;padding:30px;text-align:center;border-radius:8px 8px 0 0;'>
                <h1 style='color:white;margin:0;'>EduConnect</h1>
            </div>
            <div style='background:#f8f9fa;padding:30px;border-radius:0 0 8px 8px;'>
                <h2 style='color:#198754;'>✅ Account Created!</h2>
                <p>Hi {user.FirstName},</p>
                <p>An EduConnect account has been created for you by the administrator.
                   You can log in using your email address.</p>
                <div style='text-align:center;margin:30px 0;'>
                    <a href='{GetBaseUrl()}/Account/Login'
                       style='background:#0d6efd;color:white;padding:14px 30px;
                              text-decoration:none;border-radius:6px;font-weight:bold;'>
                        Login to EduConnect
                    </a>
                </div>
            </div>
        </div>";

        await _emailService.SendEmailAsync(
            user.Email,
            $"{user.FirstName} {user.LastName}",
            "EduConnect — Account Created",
            emailBody);
    }
    catch (Exception ex)
    {
        _logger.LogError("Welcome email failed: {Error}", ex.Message);
    }

    TempData["Success"] = $"{user.FirstName} {user.LastName}'s account has been created.";
    return RedirectToAction("Users");
}
```

- [ ] **Step 3: Build**

```powershell
dotnet build EduConnect.Web
```
Expected: `Build succeeded`.

- [ ] **Step 4: Commit**

```powershell
git add EduConnect.Web/Controllers/AdminController.cs
git commit -m "feat: add AddUser GET+POST actions to AdminController"
```

---

## Task 7: Create Views/Admin/AddUser.cshtml

**Files:**
- Create: `EduConnect.Web/Views/Admin/AddUser.cshtml`

- [ ] **Step 1: Create the view**

```razor
@model EduConnect.Web.ViewModels.AdminUserFormViewModel
@{
    ViewData["Title"] = "Add User";
}

<div class="d-flex justify-content-between align-items-center mb-4">
    <div>
        <h4 class="fw-bold mb-1">
            <i class="bi bi-person-plus me-2 text-primary"></i>
            Add User
        </h4>
        <small class="text-muted">Create a new user account manually</small>
    </div>
    <a href="/Admin/Users" class="btn btn-outline-secondary btn-sm">
        <i class="bi bi-arrow-left me-2"></i>Back
    </a>
</div>

<div class="card border-0 shadow-sm" style="max-width:640px;">
    <div class="card-body p-4">

        <form asp-action="AddUser" method="post">
            @Html.AntiForgeryToken()

            @if (!ViewData.ModelState.IsValid)
            {
                <div class="alert alert-danger mb-3">
                    <i class="bi bi-exclamation-triangle me-2"></i>
                    Please fix the errors below.
                </div>
            }

            <div class="row g-3">

                <div class="col-md-6">
                    <label asp-for="FirstName" class="form-label fw-semibold">First Name</label>
                    <input asp-for="FirstName" class="form-control" placeholder="First name" />
                    <span asp-validation-for="FirstName" class="text-danger small"></span>
                </div>

                <div class="col-md-6">
                    <label asp-for="LastName" class="form-label fw-semibold">Last Name</label>
                    <input asp-for="LastName" class="form-control" placeholder="Last name" />
                    <span asp-validation-for="LastName" class="text-danger small"></span>
                </div>

                <div class="col-12">
                    <label asp-for="Email" class="form-label fw-semibold">Email</label>
                    <input asp-for="Email" type="email" class="form-control" placeholder="email@adamson.edu.ph" />
                    <span asp-validation-for="Email" class="text-danger small"></span>
                </div>

                <div class="col-12">
                    <label asp-for="Password" class="form-label fw-semibold">Password</label>
                    <input asp-for="Password" type="password" class="form-control" placeholder="Minimum 6 characters" />
                    <span asp-validation-for="Password" class="text-danger small"></span>
                </div>

                <div class="col-12">
                    <label asp-for="StudentID" class="form-label fw-semibold">
                        Student ID <span class="text-muted fw-normal">(optional)</span>
                    </label>
                    <input asp-for="StudentID" class="form-control" placeholder="e.g. 2021-12345" />
                </div>

                <div class="col-md-6">
                    <label asp-for="RoleID" class="form-label fw-semibold">Role</label>
                    <select asp-for="RoleID" asp-items="Model.Roles" class="form-select">
                        <option value="">— Select role —</option>
                    </select>
                    <span asp-validation-for="RoleID" class="text-danger small"></span>
                </div>

                <div class="col-md-6">
                    <label asp-for="DepartmentTagID" class="form-label fw-semibold">Department</label>
                    <select asp-for="DepartmentTagID" asp-items="Model.Departments" class="form-select">
                        <option value="">— Select department —</option>
                    </select>
                    <span asp-validation-for="DepartmentTagID" class="text-danger small"></span>
                </div>

                <div class="col-12">
                    <div class="form-check">
                        <input asp-for="IsActive" class="form-check-input" type="checkbox" />
                        <label asp-for="IsActive" class="form-check-label">Account is active</label>
                    </div>
                </div>

                <div class="col-12 d-flex gap-2 pt-2">
                    <button type="submit" class="btn btn-primary">
                        <i class="bi bi-person-plus me-1"></i>Create User
                    </button>
                    <a href="/Admin/Users" class="btn btn-outline-secondary">Cancel</a>
                </div>

            </div>
        </form>
    </div>
</div>

@section Scripts {
    @{await Html.RenderPartialAsync("_ValidationScriptsPartial");}
}
```

- [ ] **Step 2: Build**

```powershell
dotnet build EduConnect.Web
```
Expected: `Build succeeded`.

- [ ] **Step 3: Commit**

```powershell
git add EduConnect.Web/Views/Admin/AddUser.cshtml
git commit -m "feat: add AddUser view"
```

---

## Task 8: Add EditUser Action to AdminController

**Files:**
- Modify: `EduConnect.Web/Controllers/AdminController.cs`

- [ ] **Step 1: Add `GET /Admin/EditUser/{id}` action**

Add after the AddUser POST action:

```csharp
// ═══════════════════════════════════════
//  GET: /Admin/EditUser/{id}
// ═══════════════════════════════════════
public async Task<IActionResult> EditUser(int id)
{
    if (!IsAdmin())
        return RedirectToAction("Login", "Account");

    var user = await _context.Users
        .Include(u => u.UserDepartments)
        .FirstOrDefaultAsync(u => u.UserID == id);

    if (user == null)
    {
        TempData["Error"] = "User not found.";
        return RedirectToAction("Users");
    }

    var primaryDept = user.UserDepartments
        .FirstOrDefault(ud => ud.IsPrimary);

    var model = new AdminUserFormViewModel
    {
        UserID = user.UserID,
        FirstName = user.FirstName,
        LastName = user.LastName,
        Email = user.Email,
        StudentID = user.StudentID,
        RoleID = user.RoleID,
        DepartmentTagID = primaryDept?.TagID ?? 0,
        IsActive = user.IsActive,
        Roles = (await _context.Roles.ToListAsync())
            .Select(r => new SelectListItem(r.RoleName, r.RoleID.ToString()))
            .ToList(),
        Departments = (await _context.DepartmentTags
            .Where(d => d.IsActive)
            .ToListAsync())
            .Select(d => new SelectListItem(
                $"{d.ShortName} — {d.TagName}", d.TagID.ToString()))
            .ToList()
    };

    return View(model);
}
```

- [ ] **Step 2: Add `POST /Admin/EditUser/{id}` action**

```csharp
// ═══════════════════════════════════════
//  POST: /Admin/EditUser/{id}
// ═══════════════════════════════════════
[HttpPost]
[ValidateAntiForgeryToken]
public async Task<IActionResult> EditUser(int id, AdminUserFormViewModel model)
{
    if (!IsAdmin())
        return RedirectToAction("Login", "Account");

    // Check email uniqueness (excluding this user)
    if (await _context.Users.AnyAsync(u => u.Email == model.Email && u.UserID != id))
        ModelState.AddModelError("Email", "A user with this email already exists.");

    // Password validation only when provided
    if (!string.IsNullOrWhiteSpace(model.Password) && model.Password.Length < 6)
        ModelState.AddModelError("Password", "Password must be at least 6 characters.");

    if (!ModelState.IsValid)
    {
        model.Roles = (await _context.Roles.ToListAsync())
            .Select(r => new SelectListItem(r.RoleName, r.RoleID.ToString()))
            .ToList();
        model.Departments = (await _context.DepartmentTags
            .Where(d => d.IsActive)
            .ToListAsync())
            .Select(d => new SelectListItem(
                $"{d.ShortName} — {d.TagName}", d.TagID.ToString()))
            .ToList();
        return View(model);
    }

    var user = await _context.Users
        .Include(u => u.UserDepartments)
        .FirstOrDefaultAsync(u => u.UserID == id);

    if (user == null)
    {
        TempData["Error"] = "User not found.";
        return RedirectToAction("Users");
    }

    user.FirstName = model.FirstName;
    user.LastName = model.LastName;
    user.Email = model.Email;
    user.StudentID = model.StudentID;
    user.RoleID = model.RoleID;
    user.IsActive = model.IsActive;
    user.UpdatedAt = DateTime.Now;

    if (!string.IsNullOrWhiteSpace(model.Password))
        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(model.Password);

    // Replace primary department
    var existingPrimary = user.UserDepartments
        .FirstOrDefault(ud => ud.IsPrimary);

    if (existingPrimary != null && existingPrimary.TagID != model.DepartmentTagID)
    {
        _context.UserDepartments.Remove(existingPrimary);
        _context.UserDepartments.Add(new UserDepartment
        {
            UserID = user.UserID,
            TagID = model.DepartmentTagID,
            IsPrimary = true,
            CreatedAt = DateTime.Now
        });
    }
    else if (existingPrimary == null)
    {
        _context.UserDepartments.Add(new UserDepartment
        {
            UserID = user.UserID,
            TagID = model.DepartmentTagID,
            IsPrimary = true,
            CreatedAt = DateTime.Now
        });
    }

    await _context.SaveChangesAsync();

    TempData["Success"] = $"{user.FirstName} {user.LastName}'s account has been updated.";
    return RedirectToAction("Users");
}
```

- [ ] **Step 3: Build**

```powershell
dotnet build EduConnect.Web
```
Expected: `Build succeeded`.

- [ ] **Step 4: Commit**

```powershell
git add EduConnect.Web/Controllers/AdminController.cs
git commit -m "feat: add EditUser GET+POST actions to AdminController"
```

---

## Task 9: Create Views/Admin/EditUser.cshtml

**Files:**
- Create: `EduConnect.Web/Views/Admin/EditUser.cshtml`

- [ ] **Step 1: Create the view**

```razor
@model EduConnect.Web.ViewModels.AdminUserFormViewModel
@{
    ViewData["Title"] = "Edit User";
}

<div class="d-flex justify-content-between align-items-center mb-4">
    <div>
        <h4 class="fw-bold mb-1">
            <i class="bi bi-pencil-square me-2 text-primary"></i>
            Edit User
        </h4>
        <small class="text-muted">Update account details</small>
    </div>
    <a href="/Admin/Users" class="btn btn-outline-secondary btn-sm">
        <i class="bi bi-arrow-left me-2"></i>Back
    </a>
</div>

<div class="card border-0 shadow-sm" style="max-width:640px;">
    <div class="card-body p-4">

        <form asp-action="EditUser" asp-route-id="@Model.UserID" method="post">
            @Html.AntiForgeryToken()

            @if (!ViewData.ModelState.IsValid)
            {
                <div class="alert alert-danger mb-3">
                    <i class="bi bi-exclamation-triangle me-2"></i>
                    Please fix the errors below.
                </div>
            }

            <div class="row g-3">

                <div class="col-md-6">
                    <label asp-for="FirstName" class="form-label fw-semibold">First Name</label>
                    <input asp-for="FirstName" class="form-control" />
                    <span asp-validation-for="FirstName" class="text-danger small"></span>
                </div>

                <div class="col-md-6">
                    <label asp-for="LastName" class="form-label fw-semibold">Last Name</label>
                    <input asp-for="LastName" class="form-control" />
                    <span asp-validation-for="LastName" class="text-danger small"></span>
                </div>

                <div class="col-12">
                    <label asp-for="Email" class="form-label fw-semibold">Email</label>
                    <input asp-for="Email" type="email" class="form-control" />
                    <span asp-validation-for="Email" class="text-danger small"></span>
                </div>

                <div class="col-12">
                    <label asp-for="Password" class="form-label fw-semibold">
                        New Password <span class="text-muted fw-normal">(leave blank to keep current)</span>
                    </label>
                    <input asp-for="Password" type="password" class="form-control"
                           placeholder="Leave blank to keep existing password" />
                    <span asp-validation-for="Password" class="text-danger small"></span>
                </div>

                <div class="col-12">
                    <label asp-for="StudentID" class="form-label fw-semibold">
                        Student ID <span class="text-muted fw-normal">(optional)</span>
                    </label>
                    <input asp-for="StudentID" class="form-control" />
                </div>

                <div class="col-md-6">
                    <label asp-for="RoleID" class="form-label fw-semibold">Role</label>
                    <select asp-for="RoleID" asp-items="Model.Roles" class="form-select"></select>
                    <span asp-validation-for="RoleID" class="text-danger small"></span>
                </div>

                <div class="col-md-6">
                    <label asp-for="DepartmentTagID" class="form-label fw-semibold">Department</label>
                    <select asp-for="DepartmentTagID" asp-items="Model.Departments" class="form-select"></select>
                    <span asp-validation-for="DepartmentTagID" class="text-danger small"></span>
                </div>

                <div class="col-12">
                    <div class="form-check">
                        <input asp-for="IsActive" class="form-check-input" type="checkbox" />
                        <label asp-for="IsActive" class="form-check-label">Account is active</label>
                    </div>
                </div>

                <div class="col-12 d-flex gap-2 pt-2">
                    <button type="submit" class="btn btn-primary">
                        <i class="bi bi-check-lg me-1"></i>Save Changes
                    </button>
                    <a href="/Admin/Users" class="btn btn-outline-secondary">Cancel</a>
                </div>

            </div>
        </form>
    </div>
</div>

@section Scripts {
    @{await Html.RenderPartialAsync("_ValidationScriptsPartial");}
}
```

- [ ] **Step 2: Build**

```powershell
dotnet build EduConnect.Web
```
Expected: `Build succeeded`.

- [ ] **Step 3: Commit**

```powershell
git add EduConnect.Web/Views/Admin/EditUser.cshtml
git commit -m "feat: add EditUser view"
```

---

## Task 10: Add DeleteUser Action to AdminController

**Files:**
- Modify: `EduConnect.Web/Controllers/AdminController.cs`

- [ ] **Step 1: Add `POST /Admin/DeleteUser/{id}` action**

Add after the EditUser POST action:

```csharp
// ═══════════════════════════════════════
//  POST: /Admin/DeleteUser/{id}
// ═══════════════════════════════════════
[HttpPost]
[ValidateAntiForgeryToken]
public async Task<IActionResult> DeleteUser(int id)
{
    if (!IsAdmin())
        return RedirectToAction("Login", "Account");

    var adminID = int.Parse(HttpContext.Session.GetString("UserID"));
    if (id == adminID)
    {
        TempData["Error"] = "You cannot delete your own account.";
        return RedirectToAction("Users");
    }

    var user = await _context.Users
        .Include(u => u.Announcements)
        .Include(u => u.OrganizedEvents)
        .FirstOrDefaultAsync(u => u.UserID == id);

    if (user == null)
    {
        TempData["Error"] = "User not found.";
        return RedirectToAction("Users");
    }

    // Block deletion if user has content that can't be safely removed
    var hasVerifiedOthers = await _context.Users
        .AnyAsync(u => u.VerifiedByID == id);
    var hasApprovedAnnouncements = await _context.Announcements
        .AnyAsync(a => a.ApprovedByID == id || a.ChairApprovedByID == id);
    var hasCreatedStudyGroups = await _context.StudyGroups
        .AnyAsync(g => g.CreatedByID == id);
    var hasIncidentReports = await _context.IncidentReports
        .AnyAsync(r => r.ReportedByID == id || r.HandledByID == id);
    var hasOrgAnnouncements = await _context.OrgAnnouncements
        .AnyAsync(a => a.PostedByID == id);

    if (user.Announcements.Any() ||
        user.OrganizedEvents.Any() ||
        hasVerifiedOthers ||
        hasApprovedAnnouncements ||
        hasCreatedStudyGroups ||
        hasIncidentReports ||
        hasOrgAnnouncements)
    {
        TempData["Error"] =
            $"Cannot delete {user.FirstName} {user.LastName} — " +
            "this user has content records (announcements, events, study groups, " +
            "incident reports, or approvals) that prevent deletion.";
        return RedirectToAction("Users");
    }

    // Remove all cleanable child records in FK-safe order
    _context.UserAnnouncementInteractions.RemoveRange(
        _context.UserAnnouncementInteractions.Where(i => i.UserID == id));

    _context.Notifications.RemoveRange(
        _context.Notifications.Where(n => n.UserID == id));

    _context.EventWaitlist.RemoveRange(
        _context.EventWaitlist.Where(w => w.UserID == id));

    _context.EventRegistrations.RemoveRange(
        _context.EventRegistrations.Where(r => r.UserID == id));

    _context.OrgMembers.RemoveRange(
        _context.OrgMembers.Where(m => m.UserID == id));

    _context.StudyGroupMembers.RemoveRange(
        _context.StudyGroupMembers.Where(m => m.UserID == id));

    _context.GroupMessages.RemoveRange(
        _context.GroupMessages.Where(m => m.SenderID == id));

    _context.GroupMembers.RemoveRange(
        _context.GroupMembers.Where(m => m.UserID == id));

    _context.Feedbacks.RemoveRange(
        _context.Feedbacks.Where(f => f.UserID == id));

    _context.ChatbotConversations.RemoveRange(
        _context.ChatbotConversations.Where(c => c.UserID == id));

    _context.RefreshTokens.RemoveRange(
        _context.RefreshTokens.Where(t => t.UserID == id));

    _context.AuditLogs.RemoveRange(
        _context.AuditLogs.Where(l => l.UserID == id));

    _context.UserDepartments.RemoveRange(
        _context.UserDepartments.Where(d => d.UserID == id));

    _context.Users.Remove(user);
    await _context.SaveChangesAsync();

    TempData["Success"] =
        $"{user.FirstName} {user.LastName}'s account has been permanently deleted.";
    return RedirectToAction("Users");
}
```

> **Property name check:** `GroupMessage.SenderID` — verify this is the actual property name in the `GroupMessage` model before running. All other DbSet and property names have been verified against `ApplicationDbContext` and model files.

- [ ] **Step 2: Build**

```powershell
dotnet build EduConnect.Web
```
Expected: `Build succeeded`. If any DbSet names are wrong the compiler will tell you exactly which ones.

- [ ] **Step 3: Commit**

```powershell
git add EduConnect.Web/Controllers/AdminController.cs
git commit -m "feat: add DeleteUser action with FK-safe hard delete"
```

---

## Task 11: Update Admin/Users.cshtml — Edit & Delete Buttons

**Files:**
- Modify: `EduConnect.Web/Views/Admin/Users.cshtml`

- [ ] **Step 1: Add "Add User" button to the page header**

Find the header section (around line 12–29) and add an "Add User" button:

```razor
<div class="d-flex justify-content-between align-items-center mb-4">
    <div>
        <h4 class="fw-bold mb-1">
            <i class="bi bi-people me-2 text-primary"></i>
            Manage Users
        </h4>
        <small class="text-muted">@users.Count total users</small>
    </div>
    <div class="d-flex gap-2">
        <a href="/Admin/AddUser" class="btn btn-primary btn-sm">
            <i class="bi bi-person-plus me-1"></i>Add User
        </a>
        <a href="/Admin" class="btn btn-outline-secondary btn-sm">
            <i class="bi bi-arrow-left me-2"></i>Back
        </a>
    </div>
</div>
```

- [ ] **Step 2: Replace the Actions column cell content**

Find the `<!-- Actions -->` `<td>` block (around line 259–277) and replace the entire `<td>...</td>` with:

```razor
<!-- Actions -->
<td>
    <div class="d-flex gap-1">
        <a href="/Admin/EditUser/@user.UserID"
           class="btn btn-sm btn-outline-primary">
            <i class="bi bi-pencil"></i>
        </a>
        <button type="button"
                class="btn btn-sm btn-outline-danger"
                data-bs-toggle="modal"
                data-bs-target="#deleteModal-@user.UserID">
            <i class="bi bi-trash"></i>
        </button>
    </div>
</td>
```

- [ ] **Step 3: Remove the Role column inline form**

Find the `<!-- Role -->` `<td>` block (lines 197–219) and replace it with a simple read-only display:

```razor
<!-- Role -->
<td>
    <span class="badge bg-primary bg-opacity-10
                 text-primary border border-primary
                 border-opacity-25 small">
        @user.Role.RoleName
    </span>
</td>
```

- [ ] **Step 4: Add delete confirmation modals**

Add after the closing `</table>` tag and before `</div></div>` of the card:

```razor
@foreach (var user in users)
{
    <div class="modal fade" id="deleteModal-@user.UserID"
         tabindex="-1" aria-hidden="true">
        <div class="modal-dialog modal-dialog-centered">
            <div class="modal-content">
                <div class="modal-header border-0">
                    <h5 class="modal-title fw-bold text-danger">
                        <i class="bi bi-exclamation-triangle me-2"></i>
                        Delete User
                    </h5>
                    <button type="button" class="btn-close"
                            data-bs-dismiss="modal"></button>
                </div>
                <div class="modal-body">
                    <p>Permanently delete
                        <strong>@user.FirstName @user.LastName</strong>?
                    </p>
                    <p class="text-muted small mb-0">
                        This action cannot be undone. Users with authored
                        announcements or organized events cannot be deleted.
                    </p>
                </div>
                <div class="modal-footer border-0">
                    <button type="button" class="btn btn-outline-secondary"
                            data-bs-dismiss="modal">Cancel</button>
                    <form asp-action="DeleteUser" asp-route-id="@user.UserID"
                          method="post" class="d-inline">
                        @Html.AntiForgeryToken()
                        <button type="submit" class="btn btn-danger">
                            <i class="bi bi-trash me-1"></i>Delete
                        </button>
                    </form>
                </div>
            </div>
        </div>
    </div>
}
```

- [ ] **Step 5: Add error TempData alert (alongside the existing success alert)**

After the success alert block (around line 32–43), add:

```razor
@if (TempData["Error"] != null)
{
    <div class="alert alert-danger alert-dismissible mb-4">
        <i class="bi bi-exclamation-triangle me-2"></i>
        @TempData["Error"]
        <button type="button" class="btn-close" data-bs-dismiss="alert"></button>
    </div>
}
```

- [ ] **Step 6: Build**

```powershell
dotnet build EduConnect.Web
```
Expected: `Build succeeded`.

- [ ] **Step 7: Commit**

```powershell
git add EduConnect.Web/Views/Admin/Users.cshtml
git commit -m "feat: update Users list with Edit/Delete buttons and confirm modals"
```

---

## Task 12: Trim Admin Sidebar and Nav in _Layout.cshtml

**Files:**
- Modify: `EduConnect.Web/Views/Shared/_Layout.cshtml`

- [ ] **Step 1: Replace the Administrator sidebar block**

Find the `@if (currentRole == "Administrator")` block (lines 230–302) and replace it with:

```razor
@if (currentRole == "Administrator")
{
    <li class="nav-item">
        <a href="/Admin" class="nav-link text-dark">
            <i class="bi bi-speedometer2 me-2"></i>
            Dashboard
        </a>
    </li>
    <li class="nav-item">
        <a href="/Admin/PendingUsers" class="nav-link text-dark">
            <i class="bi bi-person-check me-2"></i>
            Verify Students
        </a>
    </li>
    <li class="nav-item">
        <a href="/Admin/Users" class="nav-link text-dark">
            <i class="bi bi-people me-2"></i>
            Manage Users
        </a>
    </li>
    <li class="nav-item">
        <a href="/Admin/AddUser" class="nav-link text-dark">
            <i class="bi bi-person-plus me-2"></i>
            Add User
        </a>
    </li>
}
```

- [ ] **Step 2: Remove Administrator from the top navbar QR Scanner link**

Find the navbar QR Scanner condition (around line 70–78):
```razor
@if (navRole == "Administrator" || navRole == "Faculty" ||
     navRole == "Dean" || navRole == "Chair Person")
```
Change to:
```razor
@if (navRole == "Faculty" ||
     navRole == "Dean" || navRole == "Chair Person")
```

- [ ] **Step 3: Build**

```powershell
dotnet build EduConnect.Web
```
Expected: `Build succeeded`.

- [ ] **Step 4: Commit**

```powershell
git add EduConnect.Web/Views/Shared/_Layout.cshtml
git commit -m "feat: trim admin sidebar to user-management links only"
```

---

## Task 13: Remove Obsolete AdminController Actions

**Files:**
- Modify: `EduConnect.Web/Controllers/AdminController.cs`

The `ChangeUserRole` and `ToggleUserActive` POST actions are now redundant — role and active status are edited through `EditUser`. Remove them to avoid dead endpoints.

- [ ] **Step 1: Delete the `ChangeUserRole` action**

Remove the entire `ChangeUserRole` POST action (lines ~462–494 in the original file).

- [ ] **Step 2: Delete the `ToggleUserActive` action**

Remove the entire `ToggleUserActive` POST action (lines ~431–456 in the original file).

- [ ] **Step 3: Build**

```powershell
dotnet build EduConnect.Web
```
Expected: `Build succeeded`.

- [ ] **Step 4: Commit**

```powershell
git add EduConnect.Web/Controllers/AdminController.cs
git commit -m "refactor: remove obsolete ChangeUserRole and ToggleUserActive actions"
```

---

## Task 14: Manual Smoke Test

No automated tests exist in this project. Verify the following flows manually by running the app:

```powershell
dotnet run --project EduConnect.Web
```
Navigate to `https://localhost:7135`.

- [ ] **Log in as Administrator**
  - Sidebar shows only: Dashboard, Verify Students, Manage Users, Add User
  - Top navbar QR Scanner link is not visible

- [ ] **Dashboard**
  - Stat cards show user counts (Verified, Pending, Students, Faculty, Staff, Dean/Chair)
  - Bar chart shows monthly registration data
  - Doughnut chart shows users by role
  - No announcement charts or "New Announcement" button visible

- [ ] **Add User flow**
  - Navigate to `/Admin/AddUser`
  - Submit with blank password → validation error shown
  - Submit with duplicate email → validation error shown
  - Submit valid data → user appears in `/Admin/Users` list, welcome email attempted

- [ ] **Edit User flow**
  - Click Edit on any user in `/Admin/Users`
  - Change name, role, department → saved correctly
  - Leave password blank → existing password still works on next login
  - Change password → new password works on next login

- [ ] **Delete User flow**
  - Click Delete on a user who has no announcements/events → modal appears → confirm → user removed from list
  - Click Delete on a user who has authored announcements → error message shown, user not deleted
  - Try to delete your own admin account → error message shown

- [ ] **Permission gates**
  - While logged in as Administrator, navigate directly to `/Announcement/Create` → redirected away (not 200 OK)
  - Navigate to `/Event/Create` → redirected away

- [ ] **Commit final verification note**

```powershell
git commit --allow-empty -m "test: manual smoke test passed for admin redesign"
```
