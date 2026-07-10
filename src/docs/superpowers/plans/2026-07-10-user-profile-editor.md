# User Profile Editor Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a self-service profile page at `/Account/Profile` where users can upload a profile picture, set a name suffix, view their read-only details, and change their password — with the uploaded avatar replacing the generated one in the nav and sidebar.

**Architecture:** All changes live in the single `EduConnect.Web` project. Session-based auth guard (no `[Authorize]` — matches every other controller). File uploads follow the existing event cover-photo pattern (`wwwroot/uploads/avatars/`). One new EF migration adds `Suffix` to `Users`.

**Tech Stack:** ASP.NET Core 8.0 MVC, EF Core / SQL Server Express, BCrypt.Net, Bootstrap 5.1 (ec-* design system), Razor views.

## Global Constraints

- Bootstrap is **v5.1.0** — no `--bs-btn-*`/`--bs-link-*` CSS variables, no `text-bg-*` utilities. Use direct CSS properties and existing `ec-*` classes only.
- Namespace for ViewModels is `EduConnect.Web.ViewModels` (the folder is named `ViewModel` but the namespace has an `s`).
- Session guard: check `HttpContext.Session.GetString("UserID") != null`; redirect to `Account/Login` when null.
- File uploads: allowed extensions `.jpg .jpeg .png .gif .webp`, max 5 MB, save to `wwwroot/uploads/avatars/`.
- No automated tests — verification steps are build + manual browser checks.
- Run command: `dotnet run --project EduConnect.Web` (HTTPS on https://localhost:7135). Build: `dotnet build EduConnect.Web`.
- Stop the dev server before building (exe lock).

---

### Task 1: Add Suffix to User model and apply migration

**Files:**
- Modify: `EduConnect.Web/Models/User.cs`
- Generated: `EduConnect.Web/Migrations/<timestamp>_AddUserSuffix.cs` (created by EF CLI)

**Interfaces:**
- Produces: `User.Suffix` (`string?`, MaxLength 10) — used by Tasks 3 and 5.

- [ ] **Step 1: Add Suffix property to User model**

Open `EduConnect.Web/Models/User.cs`. After the `ProfilePicture` property (line 38), add:

```csharp
[MaxLength(10)]
public string? Suffix { get; set; }
```

The relevant section becomes:
```csharp
[MaxLength(500)]
public string? ProfilePicture { get; set; }

[MaxLength(10)]
public string? Suffix { get; set; }
```

- [ ] **Step 2: Generate the EF migration**

```bash
dotnet ef migrations add AddUserSuffix --project EduConnect.Web
```

Expected: `Done. To undo this action, use 'ef migrations remove'`

- [ ] **Step 3: Apply the migration to the local DB**

```bash
dotnet ef database update --project EduConnect.Web
```

Expected: last line contains `Done.`

- [ ] **Step 4: Build to confirm no errors**

```bash
dotnet build EduConnect.Web
```

Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 5: Commit**

```bash
git add EduConnect.Web/Models/User.cs EduConnect.Web/Migrations/
git commit -m "feat: add Suffix column to Users table"
```

---

### Task 2: Create ViewModels

**Files:**
- Create: `EduConnect.Web/ViewModel/ProfileViewModel.cs`
- Create: `EduConnect.Web/ViewModel/ChangePasswordViewModel.cs`

**Interfaces:**
- Produces: `ProfileViewModel` — consumed by Task 3 (controller) and Task 5 (view).
- Produces: `ChangePasswordViewModel` — consumed by Task 4 (controller) and Task 5 (view).

- [ ] **Step 1: Create ProfileViewModel**

Create `EduConnect.Web/ViewModel/ProfileViewModel.cs`:

```csharp
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace EduConnect.Web.ViewModels
{
    public class ProfileViewModel
    {
        // Read-only display fields
        public string FullName { get; set; }
        public string? StudentID { get; set; }
        public string Email { get; set; }
        public string RoleName { get; set; }
        public string? ProfilePicturePath { get; set; }

        // Editable fields
        [MaxLength(10)]
        public string? Suffix { get; set; }

        [DataType(DataType.Upload)]
        public IFormFile? NewProfilePicture { get; set; }
    }
}
```

- [ ] **Step 2: Create ChangePasswordViewModel**

Create `EduConnect.Web/ViewModel/ChangePasswordViewModel.cs`:

```csharp
using System.ComponentModel.DataAnnotations;

namespace EduConnect.Web.ViewModels
{
    public class ChangePasswordViewModel
    {
        [Required(ErrorMessage = "Current password is required")]
        [DataType(DataType.Password)]
        public string CurrentPassword { get; set; }

        [Required(ErrorMessage = "New password is required")]
        [MinLength(8, ErrorMessage = "Password must be at least 8 characters")]
        [DataType(DataType.Password)]
        public string NewPassword { get; set; }

        [Required(ErrorMessage = "Please confirm your new password")]
        [DataType(DataType.Password)]
        [Compare("NewPassword", ErrorMessage = "Passwords do not match")]
        public string ConfirmPassword { get; set; }
    }
}
```

- [ ] **Step 3: Build to confirm no errors**

```bash
dotnet build EduConnect.Web
```

Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 4: Commit**

```bash
git add EduConnect.Web/ViewModel/ProfileViewModel.cs EduConnect.Web/ViewModel/ChangePasswordViewModel.cs
git commit -m "feat: add ProfileViewModel and ChangePasswordViewModel"
```

---

### Task 3: AccountController — Login session update + Profile GET/POST

**Files:**
- Modify: `EduConnect.Web/Controllers/AccountController.cs`

**Interfaces:**
- Consumes: `ProfileViewModel` (Task 2), `User.Suffix` (Task 1), `User.ProfilePicture` (existing).
- Produces: session key `ProfilePicture` (string) — consumed by Task 6 (shared partials). Produces `GET /Account/Profile` and `POST /Account/Profile` routes — consumed by Task 5 (view).

- [ ] **Step 1: Add ProfilePicture to session on login**

In `AccountController.cs`, find the `Login` POST action. After the line that sets `RoleName` in session (around line 122), add one line:

```csharp
HttpContext.Session.SetString("RoleName",
    user.Role.RoleName);

// ADD THIS LINE:
HttpContext.Session.SetString("ProfilePicture",
    user.ProfilePicture ?? "");
```

- [ ] **Step 2: Add GET Profile action**

Add this method to `AccountController` before the `ForgotPassword` action:

```csharp
// ─── GET: /Account/Profile ────────────
[HttpGet]
public async Task<IActionResult> Profile()
{
    var userIdStr = HttpContext.Session.GetString("UserID");
    if (userIdStr == null)
        return RedirectToAction("Login");

    var user = await _context.Users
        .Include(u => u.Role)
        .FirstOrDefaultAsync(u =>
            u.UserID == int.Parse(userIdStr));

    if (user == null)
        return RedirectToAction("Login");

    var model = new ProfileViewModel
    {
        FullName = $"{user.FirstName} {(string.IsNullOrEmpty(user.Suffix) ? "" : user.Suffix + " ")}{user.LastName}".Trim(),
        StudentID = user.StudentID,
        Email = user.Email,
        RoleName = user.Role.RoleName,
        ProfilePicturePath = user.ProfilePicture,
        Suffix = user.Suffix
    };

    return View(model);
}
```

- [ ] **Step 3: Add POST Profile action**

Add this method immediately after the GET Profile action:

```csharp
// ─── POST: /Account/Profile ───────────
[HttpPost]
[ValidateAntiForgeryToken]
public async Task<IActionResult> Profile(
    ProfileViewModel model)
{
    var userIdStr = HttpContext.Session.GetString("UserID");
    if (userIdStr == null)
        return RedirectToAction("Login");

    var user = await _context.Users
        .Include(u => u.Role)
        .FirstOrDefaultAsync(u =>
            u.UserID == int.Parse(userIdStr));

    if (user == null)
        return RedirectToAction("Login");

    // ─── Handle profile picture upload ────
    if (model.NewProfilePicture != null &&
        model.NewProfilePicture.Length > 0)
    {
        var allowedTypes = new[]
        {
            ".jpg", ".jpeg",
            ".png", ".gif", ".webp"
        };

        var extension = Path.GetExtension(
            model.NewProfilePicture.FileName ?? string.Empty)
            .ToLowerInvariant();

        if (!allowedTypes.Contains(extension))
        {
            ModelState.AddModelError("NewProfilePicture",
                "Only image files are allowed (JPG, PNG, GIF, WebP).");
        }
        else if (model.NewProfilePicture.Length > 5 * 1024 * 1024)
        {
            ModelState.AddModelError("NewProfilePicture",
                "File size cannot exceed 5 MB.");
        }
        else
        {
            var uploadsFolder = Path.Combine(
                _environment.WebRootPath, "uploads", "avatars");
            Directory.CreateDirectory(uploadsFolder);

            // Delete old avatar file if it exists
            if (!string.IsNullOrEmpty(user.ProfilePicture))
            {
                var oldPath = Path.Combine(
                    _environment.WebRootPath,
                    user.ProfilePicture.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
                if (System.IO.File.Exists(oldPath))
                    System.IO.File.Delete(oldPath);
            }

            var fileName = Guid.NewGuid().ToString() + extension;
            var filePath = Path.Combine(uploadsFolder, fileName);

            using var stream = new FileStream(filePath, FileMode.Create);
            await model.NewProfilePicture.CopyToAsync(stream);

            user.ProfilePicture = "/uploads/avatars/" + fileName;
        }
    }

    if (!ModelState.IsValid)
    {
        // Re-populate read-only display fields before returning
        model.FullName = $"{user.FirstName} {(string.IsNullOrEmpty(user.Suffix) ? "" : user.Suffix + " ")}{user.LastName}".Trim();
        model.StudentID = user.StudentID;
        model.Email = user.Email;
        model.RoleName = user.Role.RoleName;
        model.ProfilePicturePath = user.ProfilePicture;
        return View(model);
    }

    // Update editable fields only
    user.Suffix = string.IsNullOrWhiteSpace(model.Suffix) ? null : model.Suffix;
    user.UpdatedAt = DateTime.Now;

    await _context.SaveChangesAsync();

    // Refresh session
    HttpContext.Session.SetString("ProfilePicture",
        user.ProfilePicture ?? "");

    TempData["Success"] = "Profile updated successfully.";
    return RedirectToAction("Profile");
}
```

- [ ] **Step 4: Add the using for ViewModels if not already present**

At the top of `AccountController.cs`, ensure this using is present:

```csharp
using EduConnect.Web.ViewModels;
```

(It should already be there from the existing Login/Register actions.)

- [ ] **Step 5: Build to confirm no errors**

```bash
dotnet build EduConnect.Web
```

Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 6: Commit**

```bash
git add EduConnect.Web/Controllers/AccountController.cs
git commit -m "feat: add Profile GET/POST to AccountController, set ProfilePicture session on login"
```

---

### Task 4: AccountController — ChangePassword POST

**Files:**
- Modify: `EduConnect.Web/Controllers/AccountController.cs`

**Interfaces:**
- Consumes: `ChangePasswordViewModel` (Task 2).
- Produces: `POST /Account/ChangePassword` route — consumed by Task 5 (view form action).

- [ ] **Step 1: Add ChangePassword POST action**

Add this method to `AccountController` immediately after the `POST Profile` action from Task 3:

```csharp
// ─── POST: /Account/ChangePassword ────
[HttpPost]
[ValidateAntiForgeryToken]
public async Task<IActionResult> ChangePassword(
    ChangePasswordViewModel model)
{
    var userIdStr = HttpContext.Session.GetString("UserID");
    if (userIdStr == null)
        return RedirectToAction("Login");

    if (!ModelState.IsValid)
    {
        TempData["ChangePasswordError"] =
            "Please fix the errors below.";
        return RedirectToAction("Profile");
    }

    var user = await _context.Users
        .FirstOrDefaultAsync(u =>
            u.UserID == int.Parse(userIdStr));

    if (user == null)
        return RedirectToAction("Login");

    // Verify current password
    bool currentValid = BCrypt.Net.BCrypt
        .Verify(model.CurrentPassword, user.PasswordHash);

    if (!currentValid)
    {
        TempData["ChangePasswordError"] =
            "Current password is incorrect.";
        return RedirectToAction("Profile");
    }

    // Set new password
    user.PasswordHash = BCrypt.Net.BCrypt
        .HashPassword(model.NewPassword);
    user.UpdatedAt = DateTime.Now;

    await _context.SaveChangesAsync();

    _logger.LogInformation(
        "User {UserID} changed their password.",
        user.UserID);

    TempData["Success"] = "Password changed successfully.";
    return RedirectToAction("Profile");
}
```

- [ ] **Step 2: Build to confirm no errors**

```bash
dotnet build EduConnect.Web
```

Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 3: Commit**

```bash
git add EduConnect.Web/Controllers/AccountController.cs
git commit -m "feat: add ChangePassword POST action to AccountController"
```

---

### Task 5: Create Profile view

**Files:**
- Create: `EduConnect.Web/Views/Account/Profile.cshtml`

**Interfaces:**
- Consumes: `ProfileViewModel` (Task 2), routes `POST /Account/Profile` and `POST /Account/ChangePassword` (Tasks 3 & 4).

- [ ] **Step 1: Create the view file**

Create `EduConnect.Web/Views/Account/Profile.cshtml`:

```cshtml
@model EduConnect.Web.ViewModels.ProfileViewModel
@{
    ViewData["Title"] = "My Profile";
    var avatarSrc = string.IsNullOrEmpty(Model.ProfilePicturePath)
        ? $"https://ui-avatars.com/api/?name={Uri.EscapeDataString(Model.FullName)}&background=002F6C&color=fff"
        : Model.ProfilePicturePath;
}

<!-- Success / Error alerts -->
@if (TempData["Success"] != null)
{
    <div class="alert alert-success alert-dismissible fade show" role="alert">
        <i class="bi bi-check-circle me-2"></i>@TempData["Success"]
        <button type="button" class="btn-close" data-bs-dismiss="alert"></button>
    </div>
}
@if (TempData["Error"] != null)
{
    <div class="alert alert-danger alert-dismissible fade show" role="alert">
        <i class="bi bi-exclamation-circle me-2"></i>@TempData["Error"]
        <button type="button" class="btn-close" data-bs-dismiss="alert"></button>
    </div>
}
@if (TempData["ChangePasswordError"] != null)
{
    <div class="alert alert-danger alert-dismissible fade show" role="alert">
        <i class="bi bi-exclamation-circle me-2"></i>@TempData["ChangePasswordError"]
        <button type="button" class="btn-close" data-bs-dismiss="alert"></button>
    </div>
}

<h4 class="fw-bold mb-4">My Profile</h4>

<div class="row g-4">

    <!-- ── Card 1: Profile Info ─────────────── -->
    <div class="col-12 col-lg-7">
        <div class="card border-0 shadow-sm">
            <div class="card-header fw-semibold py-3"
                 style="background:var(--ec-navy);color:#fff;">
                <i class="bi bi-person-circle me-2"></i>Profile Information
            </div>
            <div class="card-body p-4">
                <form asp-action="Profile"
                      asp-controller="Account"
                      method="post"
                      enctype="multipart/form-data">
                    @Html.AntiForgeryToken()

                    <!-- Avatar -->
                    <div class="d-flex align-items-center gap-4 mb-4">
                        <img id="avatarPreview"
                             src="@avatarSrc"
                             class="rounded-circle ec-avatar-ring"
                             width="96" height="96"
                             alt="Profile picture"
                             style="object-fit:cover;" />
                        <div>
                            <label class="form-label fw-semibold mb-1 d-block">
                                Profile Picture
                            </label>
                            <input type="file"
                                   asp-for="NewProfilePicture"
                                   class="form-control form-control-sm"
                                   accept=".jpg,.jpeg,.png,.gif,.webp"
                                   onchange="previewAvatar(this)" />
                            <div class="form-text">JPG, PNG, GIF or WebP · max 5 MB</div>
                            <span asp-validation-for="NewProfilePicture"
                                  class="text-danger small"></span>
                        </div>
                    </div>

                    <!-- Full Name (read-only) -->
                    <div class="mb-3">
                        <label class="form-label fw-semibold">Full Name</label>
                        <input type="text"
                               class="form-control"
                               value="@Model.FullName"
                               disabled />
                    </div>

                    @if (!string.IsNullOrEmpty(Model.StudentID))
                    {
                        <!-- Student No. (read-only) -->
                        <div class="mb-3">
                            <label class="form-label fw-semibold">Student No.</label>
                            <input type="text"
                                   class="form-control"
                                   value="@Model.StudentID"
                                   disabled />
                        </div>
                    }

                    <!-- Email (read-only) -->
                    <div class="mb-3">
                        <label class="form-label fw-semibold">Email</label>
                        <input type="email"
                               class="form-control"
                               value="@Model.Email"
                               disabled />
                    </div>

                    <!-- Role (read-only) -->
                    <div class="mb-3">
                        <label class="form-label fw-semibold">Role</label>
                        <input type="text"
                               class="form-control"
                               value="@Model.RoleName"
                               disabled />
                    </div>

                    <!-- Suffix (editable) -->
                    <div class="mb-4">
                        <label asp-for="Suffix"
                               class="form-label fw-semibold">Name Suffix</label>
                        <select asp-for="Suffix" class="form-select">
                            <option value="">None</option>
                            <option value="Jr.">Jr.</option>
                            <option value="Sr.">Sr.</option>
                            <option value="II">II</option>
                            <option value="III">III</option>
                            <option value="IV">IV</option>
                            <option value="V">V</option>
                        </select>
                        <span asp-validation-for="Suffix"
                              class="text-danger small"></span>
                    </div>

                    <button type="submit"
                            class="btn ec-btn-primary px-4">
                        <i class="bi bi-check-lg me-2"></i>Save Changes
                    </button>
                </form>
            </div>
        </div>
    </div>

    <!-- ── Card 2: Change Password ──────────── -->
    <div class="col-12 col-lg-5">
        <div class="card border-0 shadow-sm">
            <div class="card-header fw-semibold py-3"
                 style="background:var(--ec-navy);color:#fff;">
                <i class="bi bi-shield-lock me-2"></i>Change Password
            </div>
            <div class="card-body p-4">
                <form asp-action="ChangePassword"
                      asp-controller="Account"
                      method="post">
                    @Html.AntiForgeryToken()

                    <!-- Current Password -->
                    <div class="mb-3">
                        <label class="form-label fw-semibold">Current Password</label>
                        <div class="input-group">
                            <span class="input-group-text">
                                <i class="bi bi-lock"></i>
                            </span>
                            <input type="password"
                                   name="CurrentPassword"
                                   class="form-control"
                                   id="currPwd"
                                   placeholder="Enter current password" />
                            <button class="btn btn-outline-secondary"
                                    type="button"
                                    onclick="togglePwd('currPwd','eyeCurr')">
                                <i class="bi bi-eye" id="eyeCurr"></i>
                            </button>
                        </div>
                    </div>

                    <!-- New Password -->
                    <div class="mb-3">
                        <label class="form-label fw-semibold">New Password</label>
                        <div class="input-group">
                            <span class="input-group-text">
                                <i class="bi bi-lock-fill"></i>
                            </span>
                            <input type="password"
                                   name="NewPassword"
                                   class="form-control"
                                   id="newPwd"
                                   placeholder="Minimum 8 characters"
                                   minlength="8" />
                            <button class="btn btn-outline-secondary"
                                    type="button"
                                    onclick="togglePwd('newPwd','eyeNew')">
                                <i class="bi bi-eye" id="eyeNew"></i>
                            </button>
                        </div>
                    </div>

                    <!-- Confirm Password -->
                    <div class="mb-4">
                        <label class="form-label fw-semibold">Confirm New Password</label>
                        <div class="input-group">
                            <span class="input-group-text">
                                <i class="bi bi-lock-fill"></i>
                            </span>
                            <input type="password"
                                   name="ConfirmPassword"
                                   class="form-control"
                                   id="confPwd"
                                   placeholder="Re-enter new password"
                                   minlength="8" />
                            <button class="btn btn-outline-secondary"
                                    type="button"
                                    onclick="togglePwd('confPwd','eyeConf')">
                                <i class="bi bi-eye" id="eyeConf"></i>
                            </button>
                        </div>
                    </div>

                    <button type="submit"
                            class="btn ec-btn-primary px-4">
                        <i class="bi bi-check-lg me-2"></i>Change Password
                    </button>
                </form>
            </div>
        </div>
    </div>

</div>

@section Scripts {
    <script>
        function togglePwd(fieldId, iconId) {
            const f = document.getElementById(fieldId);
            const i = document.getElementById(iconId);
            if (f.type === 'password') {
                f.type = 'text';
                i.className = 'bi bi-eye-slash';
            } else {
                f.type = 'password';
                i.className = 'bi bi-eye';
            }
        }

        function previewAvatar(input) {
            if (input.files && input.files[0]) {
                const reader = new FileReader();
                reader.onload = e => {
                    document.getElementById('avatarPreview').src = e.target.result;
                };
                reader.readAsDataURL(input.files[0]);
            }
        }
    </script>
}
```

- [ ] **Step 2: Build to confirm no errors**

```bash
dotnet build EduConnect.Web
```

Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 3: Smoke-test in the browser**

Start the app and log in as `admin@educonnect.edu` / `Admin@123`. Navigate to the profile via the nav dropdown → Profile.

Verify:
- Page loads without error.
- Full Name, Email, Role are displayed and greyed-out (disabled).
- Student No. row is absent for the admin user.
- Suffix dropdown pre-selects "None".
- Both cards render with navy headers.

- [ ] **Step 4: Commit**

```bash
git add EduConnect.Web/Views/Account/Profile.cshtml
git commit -m "feat: add Profile view with profile-info and change-password cards"
```

---

### Task 6: Update shared avatar partials

**Files:**
- Modify: `EduConnect.Web/Views/Shared/_Layout.cshtml` (line ~86)
- Modify: `EduConnect.Web/Views/Shared/_SidebarContent.cshtml` (lines 3–6)

**Interfaces:**
- Consumes: session key `ProfilePicture` (set by Task 3).

- [ ] **Step 1: Update _Layout.cshtml navbar avatar**

In `_Layout.cshtml`, find the static avatar `<img>` around line 86–89:

```html
<img src="https://ui-avatars.com/api/?name=@Context.Session.GetString("UserName")&background=002F6C&color=fff"
     class="rounded-circle ec-avatar-ring"
     width="32" height="32"
     alt="Avatar" />
```

Replace with:

```cshtml
@{
    var _navPic = Context.Session.GetString("ProfilePicture");
    var _navAvatar = string.IsNullOrEmpty(_navPic)
        ? $"https://ui-avatars.com/api/?name={Uri.EscapeDataString(Context.Session.GetString("UserName") ?? "")}&background=002F6C&color=fff"
        : _navPic;
}
<img src="@_navAvatar"
     class="rounded-circle ec-avatar-ring"
     width="32" height="32"
     alt="Avatar"
     style="object-fit:cover;" />
```

- [ ] **Step 2: Update _SidebarContent.cshtml avatar**

In `_SidebarContent.cshtml`, find the avatar `<img>` at lines 3–6:

```html
<img src="https://ui-avatars.com/api/?name=@(Context.Session.GetString("UserName") ?? "Guest")&background=002F6C&color=fff"
     class="rounded-circle ec-avatar-ring"
     width="45" height="45"
     alt="Avatar" />
```

Replace with:

```cshtml
@{
    var _sidePic = Context.Session.GetString("ProfilePicture");
    var _sideAvatar = string.IsNullOrEmpty(_sidePic)
        ? $"https://ui-avatars.com/api/?name={Uri.EscapeDataString(Context.Session.GetString("UserName") ?? "Guest")}&background=002F6C&color=fff"
        : _sidePic;
}
<img src="@_sideAvatar"
     class="rounded-circle ec-avatar-ring"
     width="45" height="45"
     alt="Avatar"
     style="object-fit:cover;" />
```

- [ ] **Step 3: Build to confirm no errors**

```bash
dotnet build EduConnect.Web
```

Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 4: End-to-end browser verification**

Start the app and log in. Then:

1. Go to Profile → upload a photo → click Save Changes.
2. Verify the nav avatar (top-right, 32px) updates to the photo.
3. Open the sidebar — verify the 45px sidebar avatar also shows the photo.
4. Open the drawer on mobile width — verify it too.
5. Log out and back in — verify the photo still appears (loaded from DB → session on login).
6. Go to Profile → Change Password card → enter wrong current password → submit → verify error message.
7. Enter correct current password, new password, matching confirm → submit → verify success message.
8. Log out → log in with the new password — verify it works.
9. Log in as a student account — verify the Student No. row appears on their profile page.

- [ ] **Step 5: Commit**

```bash
git add EduConnect.Web/Views/Shared/_Layout.cshtml EduConnect.Web/Views/Shared/_SidebarContent.cshtml
git commit -m "feat: use uploaded profile picture in nav and sidebar avatars"
```
