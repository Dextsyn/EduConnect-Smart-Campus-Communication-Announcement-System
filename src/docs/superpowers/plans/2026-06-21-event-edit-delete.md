# Event Edit & Delete Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let event creators (Faculty/Dean/Chair Person) edit and delete their own events; deleting cancels the event and emails all registered attendees.

**Architecture:** Six focused changes — ViewModel properties, controller helpers + ViewBag wiring, two new controller action pairs (Edit GET/POST, Delete POST), a new Edit view cloned from Create, and small additions to Details and Index views.

**Tech Stack:** ASP.NET Core 8 MVC, EF Core, Razor, Bootstrap 5, BCrypt (not touched), MailKit via `IEmailService`

---

## File Map

| File | Change |
|---|---|
| `EduConnect.Web/ViewModel/EventFormViewModel.cs` | Add `RemoveCoverPhoto` bool; add `OrganizerID` to `EventListViewModel` |
| `EduConnect.Web/Controllers/EventController.cs` | Add `IsCreator()` helper; update `Index` + `Details` ViewBag; add GET/POST `Edit`; add POST `Delete` |
| `EduConnect.Web/Views/Event/Edit.cshtml` | New — cloned from Create with existing-photo UI |
| `EduConnect.Web/Views/Event/Details.cshtml` | Add `isCreator` bool; gate Edit/Delete by creator; add Delete modal |
| `EduConnect.Web/Views/Event/Index.cshtml` | Read `ViewBag.CurrentUserID`; add Edit/Delete icon buttons on creator's cards |

---

## Task 1: ViewModel — add `RemoveCoverPhoto` and `OrganizerID`

**Files:**
- Modify: `EduConnect.Web/ViewModel/EventFormViewModel.cs`

- [ ] **Step 1: Add `RemoveCoverPhoto` to `EventFormViewModel` and `OrganizerID` to `EventListViewModel`**

Open `EduConnect.Web/ViewModel/EventFormViewModel.cs`.

In `EventFormViewModel` (after the `ExistingCoverPhotoURL` property), add:

```csharp
public bool RemoveCoverPhoto { get; set; }
```

In `EventListViewModel` (after the `OrganizerName` property), add:

```csharp
public int OrganizerID { get; set; }
```

- [ ] **Step 2: Build to verify no compile errors**

```powershell
dotnet build EduConnect.Web
```

Expected: `Build succeeded.`

- [ ] **Step 3: Commit**

```powershell
git add EduConnect.Web/ViewModel/EventFormViewModel.cs
git commit -m "feat: add RemoveCoverPhoto to EventFormViewModel, OrganizerID to EventListViewModel"
```

---

## Task 2: Controller — helpers and ViewBag wiring

**Files:**
- Modify: `EduConnect.Web/Controllers/EventController.cs`

- [ ] **Step 1: Add `IsCreator` helper**

In `EventController.cs`, after the `CanScan()` method (around line 66), add:

```csharp
private bool IsCreator(Event ev) =>
    ev.OrganizerID == GetUserID();
```

- [ ] **Step 2: Set `ViewBag.IsCreator` in the `Details` action**

In the `Details` action, find this block near the end (currently line ~365):

```csharp
ViewBag.IsOrganizer =
    ev.OrganizerID == userID ||
    roleName == "Administrator";
```

Replace it with:

```csharp
ViewBag.IsOrganizer =
    ev.OrganizerID == userID ||
    roleName == "Administrator";

ViewBag.IsCreator =
    ev.OrganizerID == userID;
```

- [ ] **Step 3: Populate `OrganizerID` in `EventListViewModel` and set `ViewBag.CurrentUserID` in `Index`**

In the `Index` action, find the `eventList` projection (the `events.Select(e => new EventListViewModel { ... })` block). After `OrganizerName = e.Organizer.FirstName + " " + e.Organizer.LastName`, add:

```csharp
OrganizerID = e.OrganizerID
```

Then after `ViewBag.CanManage = CanManageEvents();`, add:

```csharp
ViewBag.CurrentUserID = GetUserID();
```

- [ ] **Step 4: Build**

```powershell
dotnet build EduConnect.Web
```

Expected: `Build succeeded.`

- [ ] **Step 5: Commit**

```powershell
git add EduConnect.Web/Controllers/EventController.cs
git commit -m "feat: add IsCreator helper, wire IsCreator/CurrentUserID ViewBag in Details and Index"
```

---

## Task 3: Controller — Edit GET and POST

**Files:**
- Modify: `EduConnect.Web/Controllers/EventController.cs`

- [ ] **Step 1: Add `DeleteEventPhoto` private helper**

After the `GenerateQRCode` method (around line 98), add:

```csharp
private void DeleteEventPhoto(string? url)
{
    if (string.IsNullOrEmpty(url)) return;
    var path = Path.Combine(
        _environment.WebRootPath,
        url.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
    if (System.IO.File.Exists(path))
        System.IO.File.Delete(path);
}
```

- [ ] **Step 2: Add GET Edit action**

After the closing brace of `POST /Event/Create` (after line ~547), add:

```csharp
// ═══════════════════════════════════════
//  GET: /Event/Edit/5
// ═══════════════════════════════════════
public async Task<IActionResult> Edit(int id)
{
    if (!IsLoggedIn())
        return RedirectToAction("Login", "Account");

    if (!CanManageEvents())
        return RedirectToAction("Index");

    var ev = await _context.Events
        .FirstOrDefaultAsync(e => e.EventID == id);

    if (ev == null)
        return NotFound();

    if (!IsCreator(ev))
    {
        TempData["Error"] =
            "You can only edit events you created.";
        return RedirectToAction(
            "Details", new { id });
    }

    var model = new EventFormViewModel
    {
        EventID              = ev.EventID,
        EventTitle           = ev.EventTitle,
        Description          = ev.Description,
        Location             = ev.Location,
        StartDateTime        = ev.StartDateTime,
        EndDateTime          = ev.EndDateTime,
        MaxAttendees         = ev.MaxAttendees,
        IsOnline             = ev.IsOnline,
        MeetingURL           = ev.MeetingURL,
        RegistrationDeadline = ev.RegistrationDeadline,
        LinkedAnnouncementID = ev.AnnouncementID,
        ExistingCoverPhotoURL = ev.CoverPhotoURL,
        Announcements        = await _context.Announcements
            .Where(a => a.Status == "Published" &&
                        a.AuthorID == GetUserID())
            .OrderByDescending(a => a.PublishedAt)
            .ToListAsync()
    };

    return View(model);
}
```

- [ ] **Step 3: Add POST Edit action**

Immediately after the GET Edit action, add:

```csharp
// ═══════════════════════════════════════
//  POST: /Event/Edit/5
// ═══════════════════════════════════════
[HttpPost]
[ValidateAntiForgeryToken]
public async Task<IActionResult> Edit(
    int id, EventFormViewModel model)
{
    if (!IsLoggedIn())
        return RedirectToAction("Login", "Account");

    if (!CanManageEvents())
        return RedirectToAction("Index");

    var ev = await _context.Events
        .FirstOrDefaultAsync(e => e.EventID == id);

    if (ev == null)
        return NotFound();

    if (!IsCreator(ev))
    {
        TempData["Error"] =
            "You can only edit events you created.";
        return RedirectToAction(
            "Details", new { id });
    }

    // ─── Date validation ───────────────────────
    if (!model.StartDateTime.HasValue)
        ModelState.AddModelError(
            "StartDateTime", "Start date is required.");

    if (!model.EndDateTime.HasValue)
        ModelState.AddModelError(
            "EndDateTime", "End date is required.");

    if (model.StartDateTime.HasValue &&
        model.EndDateTime.HasValue &&
        model.EndDateTime.Value <= model.StartDateTime.Value)
    {
        ModelState.AddModelError(
            "EndDateTime",
            "End date must be after start date.");
    }

    if (!ModelState.IsValid)
    {
        model.ExistingCoverPhotoURL = ev.CoverPhotoURL;
        model.Announcements = await _context.Announcements
            .Where(a => a.Status == "Published" &&
                        a.AuthorID == GetUserID())
            .OrderByDescending(a => a.PublishedAt)
            .ToListAsync();
        return View(model);
    }

    // ─── Photo handling ────────────────────────
    if (model.RemoveCoverPhoto)
    {
        DeleteEventPhoto(ev.CoverPhotoURL);
        ev.CoverPhotoURL = null;
    }
    else if (model.CoverPhoto != null &&
             model.CoverPhoto.Length > 0)
    {
        var allowedTypes = new[]
        {
            ".jpg", ".jpeg", ".png", ".gif", ".webp"
        };
        var extension = Path.GetExtension(
            model.CoverPhoto.FileName).ToLowerInvariant();

        if (!allowedTypes.Contains(extension))
        {
            ModelState.AddModelError("CoverPhoto",
                "Only image files are allowed (JPG, PNG, GIF, WebP).");
            model.ExistingCoverPhotoURL = ev.CoverPhotoURL;
            model.Announcements = await _context.Announcements
                .Where(a => a.Status == "Published" &&
                            a.AuthorID == GetUserID())
                .OrderByDescending(a => a.PublishedAt)
                .ToListAsync();
            return View(model);
        }

        if (model.CoverPhoto.Length > 5 * 1024 * 1024)
        {
            ModelState.AddModelError("CoverPhoto",
                "File size cannot exceed 5MB.");
            model.ExistingCoverPhotoURL = ev.CoverPhotoURL;
            model.Announcements = await _context.Announcements
                .Where(a => a.Status == "Published" &&
                            a.AuthorID == GetUserID())
                .OrderByDescending(a => a.PublishedAt)
                .ToListAsync();
            return View(model);
        }

        DeleteEventPhoto(ev.CoverPhotoURL);

        var uploadsFolder = Path.Combine(
            _environment.WebRootPath, "uploads", "events");
        Directory.CreateDirectory(uploadsFolder);

        var fileName = Guid.NewGuid().ToString() + extension;
        var filePath = Path.Combine(uploadsFolder, fileName);

        using var stream = new FileStream(
            filePath, FileMode.Create);
        await model.CoverPhoto.CopyToAsync(stream);

        ev.CoverPhotoURL = "/uploads/events/" + fileName;
    }
    // else: keep existing photo unchanged

    // ─── Update fields ─────────────────────────
    ev.EventTitle            = model.EventTitle;
    ev.Description           = model.Description;
    ev.Location              = model.Location;
    ev.StartDateTime         = model.StartDateTime!.Value;
    ev.EndDateTime           = model.EndDateTime!.Value;
    ev.MaxAttendees          = model.MaxAttendees;
    ev.IsOnline              = model.IsOnline;
    ev.MeetingURL            = model.MeetingURL;
    ev.RegistrationDeadline  = model.RegistrationDeadline;
    ev.AnnouncementID        = model.LinkedAnnouncementID;
    ev.UpdatedAt             = DateTime.Now;

    await _context.SaveChangesAsync();

    TempData["Success"] = "Event updated successfully!";
    return RedirectToAction("Details", new { id });
}
```

- [ ] **Step 4: Build**

```powershell
dotnet build EduConnect.Web
```

Expected: `Build succeeded.`

- [ ] **Step 5: Commit**

```powershell
git add EduConnect.Web/Controllers/EventController.cs
git commit -m "feat: add Event Edit GET/POST with creator-only auth and photo handling"
```

---

## Task 4: Controller — Delete POST

**Files:**
- Modify: `EduConnect.Web/Controllers/EventController.cs`

- [ ] **Step 1: Add POST Delete action**

After the closing brace of `POST /Event/Edit` (added in Task 3), add:

```csharp
// ═══════════════════════════════════════
//  POST: /Event/Delete/5
//  Cancels the event; emails registrants
// ═══════════════════════════════════════
[HttpPost]
[ValidateAntiForgeryToken]
public async Task<IActionResult> Delete(int id)
{
    if (!IsLoggedIn())
        return RedirectToAction("Login", "Account");

    var ev = await _context.Events
        .Include(e => e.Registrations)
            .ThenInclude(r => r.User)
        .FirstOrDefaultAsync(e => e.EventID == id);

    if (ev == null)
        return NotFound();

    if (!IsCreator(ev))
    {
        TempData["Error"] =
            "You can only delete events you created.";
        return RedirectToAction("Details", new { id });
    }

    ev.Status              = "Cancelled";
    ev.IsRegistrationOpen  = false;
    ev.UpdatedAt           = DateTime.Now;
    await _context.SaveChangesAsync();

    // Email all registered attendees (fire-and-forget)
    var registrants = ev.Registrations
        .Where(r => r.Status == "Registered")
        .ToList();

    foreach (var reg in registrants)
    {
        try
        {
            await _emailService.SendEmailAsync(
                reg.User.Email,
                reg.User.FirstName + " " + reg.User.LastName,
                $"Event Cancelled: {ev.EventTitle}",
                $@"<div style='font-family: Arial;
                              max-width: 600px;
                              margin: 0 auto;'>
                    <div style='background: #dc3545;
                                padding: 30px;
                                text-align: center;
                                border-radius: 8px 8px 0 0;'>
                        <h1 style='color: white; margin: 0;'>
                            EduConnect
                        </h1>
                    </div>
                    <div style='background: #f8f9fa;
                                padding: 30px;
                                border-radius: 0 0 8px 8px;'>
                        <h2 style='color: #dc3545;'>
                            Event Cancelled
                        </h2>
                        <p>Hi {reg.User.FirstName},</p>
                        <p>We regret to inform you that
                           the following event has been
                           cancelled:</p>
                        <div style='background: white;
                                    padding: 20px;
                                    border-radius: 8px;
                                    border-left: 4px solid #dc3545;
                                    margin: 20px 0;'>
                            <h3 style='margin: 0 0 10px;'>
                                {ev.EventTitle}
                            </h3>
                            <p style='margin: 5px 0;
                                      color: #666;'>
                                📅 {ev.StartDateTime
                                    .ToString("MMMM dd, yyyy")}
                            </p>
                            <p style='margin: 5px 0;
                                      color: #666;'>
                                🕐 {ev.StartDateTime
                                    .ToString("hh:mm tt")} —
                                {ev.EndDateTime
                                    .ToString("hh:mm tt")}
                            </p>
                        </div>
                        <p style='color: #666;'>
                            We apologise for any inconvenience.
                        </p>
                    </div>
                </div>");
        }
        catch (Exception ex)
        {
            _logger.LogError(
                "Cancellation email failed for user {UserID}: {Error}",
                reg.UserID, ex.Message);
        }
    }

    TempData["Success"] =
        $"\"{ev.EventTitle}\" has been cancelled " +
        $"and {registrants.Count} registrant(s) notified.";
    return RedirectToAction("Index");
}
```

- [ ] **Step 2: Build**

```powershell
dotnet build EduConnect.Web
```

Expected: `Build succeeded.`

- [ ] **Step 3: Commit**

```powershell
git add EduConnect.Web/Controllers/EventController.cs
git commit -m "feat: add Event Delete POST — cancels event and emails registrants"
```

---

## Task 5: Create `Views/Event/Edit.cshtml`

**Files:**
- Create: `EduConnect.Web/Views/Event/Edit.cshtml`

- [ ] **Step 1: Create the Edit view**

Create `EduConnect.Web/Views/Event/Edit.cshtml` with this content:

```cshtml
@model EduConnect.Web.ViewModels.EventFormViewModel
@{
    ViewData["Title"] = "Edit Event";
}

<!-- ─── PAGE HEADER ──────────────────── -->
<div class="d-flex justify-content-between
            align-items-center mb-4">
    <div>
        <h4 class="fw-bold mb-1">
            <i class="bi bi-pencil-square
                       me-2 text-primary"></i>
            Edit Event
        </h4>
        <small class="text-muted">
            Update the event details below
        </small>
    </div>
    <a href="/Event/Details/@Model.EventID"
       class="btn btn-outline-secondary btn-sm">
        <i class="bi bi-arrow-left me-2"></i>
        Back
    </a>
</div>

<form asp-action="Edit"
      asp-route-id="@Model.EventID"
      method="post"
      enctype="multipart/form-data">
    @Html.AntiForgeryToken()

    <div class="row g-4">

        <!-- ─── MAIN FORM ──────────────── -->
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

                    <!-- Event Title -->
                    <div class="mb-4">
                        <label asp-for="EventTitle"
                               class="form-label fw-semibold">
                            Event Title
                            <span class="text-danger">*</span>
                        </label>
                        <input asp-for="EventTitle"
                               class="form-control form-control-lg"
                               placeholder="Enter event title..." />
                        <span asp-validation-for="EventTitle"
                              class="text-danger small"></span>
                    </div>

                    <!-- Description -->
                    <div class="mb-4">
                        <label asp-for="Description"
                               class="form-label fw-semibold">
                            Description
                            <span class="text-muted fw-normal">(optional)</span>
                        </label>
                        <textarea asp-for="Description"
                                  class="form-control"
                                  rows="5"
                                  placeholder="Describe the event..."></textarea>
                    </div>

                    <!-- Cover Photo -->
                    <div class="mb-4">
                        <label class="form-label fw-semibold">
                            Cover Photo
                            <span class="text-muted fw-normal">(optional)</span>
                        </label>

                        @if (!string.IsNullOrEmpty(
                                Model.ExistingCoverPhotoURL))
                        {
                            <div id="existingPhotoSection" class="mb-3">
                                <img src="@Model.ExistingCoverPhotoURL"
                                     id="existingPhotoThumb"
                                     class="img-thumbnail"
                                     style="max-height: 150px;" />
                                <div class="form-check mt-2">
                                    <input asp-for="RemoveCoverPhoto"
                                           class="form-check-input"
                                           type="checkbox"
                                           id="removeCoverPhoto"
                                           onchange="toggleRemovePhoto(this)" />
                                    <label class="form-check-label text-danger"
                                           for="removeCoverPhoto">
                                        Remove current photo
                                    </label>
                                </div>
                            </div>
                        }

                        <div id="newPhotoSection">
                            <input asp-for="CoverPhoto"
                                   type="file"
                                   class="form-control"
                                   accept="image/*"
                                   onchange="previewPhoto(this)" />
                            <span asp-validation-for="CoverPhoto"
                                  class="text-danger small"></span>
                        </div>

                        <div id="photoPreview" class="mt-3 d-none">
                            <img id="previewImg"
                                 class="img-fluid rounded"
                                 style="max-height: 200px;"
                                 alt="Preview" />
                        </div>
                    </div>

                    <!-- Start Date & Time -->
                    <div class="col-md-6">
                        <label asp-for="StartDateTime"
                               class="form-label fw-semibold">
                            Start Date & Time
                            <span class="text-danger">*</span>
                        </label>
                        <input asp-for="StartDateTime"
                               type="datetime-local"
                               class="form-control"
                               id="startDateTime"
                               step="60" />
                        <span asp-validation-for="StartDateTime"
                              class="text-danger small"></span>
                    </div>

                    <!-- End Date & Time -->
                    <div class="col-md-6">
                        <label asp-for="EndDateTime"
                               class="form-label fw-semibold">
                            End Date & Time
                            <span class="text-danger">*</span>
                        </label>
                        <input asp-for="EndDateTime"
                               type="datetime-local"
                               class="form-control"
                               id="endDateTime"
                               step="60" />
                        <span asp-validation-for="EndDateTime"
                              class="text-danger small"></span>
                    </div>

                    <!-- Location -->
                    <div class="mb-4">
                        <label asp-for="Location"
                               class="form-label fw-semibold">
                            Location
                        </label>
                        <div class="input-group">
                            <span class="input-group-text">
                                <i class="bi bi-geo-alt"></i>
                            </span>
                            <input asp-for="Location"
                                   class="form-control"
                                   placeholder="e.g. AVR Building A, Room 301" />
                        </div>
                    </div>

                    <!-- Online Event Toggle -->
                    <div class="mb-4">
                        <div class="form-check form-switch">
                            <input asp-for="IsOnline"
                                   class="form-check-input"
                                   type="checkbox"
                                   role="switch"
                                   id="isOnlineSwitch"
                                   onchange="toggleOnline()" />
                            <label asp-for="IsOnline"
                                   class="form-check-label fw-semibold">
                                Online Event
                            </label>
                        </div>
                    </div>

                    <!-- Meeting URL (shown if online) -->
                    <div class="mb-4" id="meetingUrlDiv"
                         style="display: none;">
                        <label asp-for="MeetingURL"
                               class="form-label fw-semibold">
                            Meeting URL
                        </label>
                        <div class="input-group">
                            <span class="input-group-text">
                                <i class="bi bi-camera-video"></i>
                            </span>
                            <input asp-for="MeetingURL"
                                   class="form-control"
                                   placeholder="https://meet.google.com/..." />
                        </div>
                    </div>

                </div>
            </div>
        </div>

        <!-- ─── SETTINGS SIDEBAR ──────── -->
        <div class="col-12 col-lg-4">

            <div class="card border-0 shadow-sm mb-4">
                <div class="card-header bg-white border-0 pt-3">
                    <h6 class="fw-bold mb-0">
                        <i class="bi bi-gear me-2 text-primary"></i>
                        Event Settings
                    </h6>
                </div>
                <div class="card-body">

                    <!-- Max Attendees -->
                    <div class="mb-3">
                        <label asp-for="MaxAttendees"
                               class="form-label fw-semibold">
                            Maximum Attendees
                            <span class="text-muted fw-normal">
                                (leave blank = unlimited)
                            </span>
                        </label>
                        <input asp-for="MaxAttendees"
                               type="number"
                               class="form-control"
                               min="1"
                               placeholder="e.g. 50" />
                    </div>

                    <!-- Registration Deadline -->
                    <div class="mb-3">
                        <label asp-for="RegistrationDeadline"
                               class="form-label fw-semibold">
                            Registration Deadline
                            <span class="text-muted fw-normal">(optional)</span>
                        </label>
                        <input asp-for="RegistrationDeadline"
                               type="datetime-local"
                               class="form-control"
                               id="regDeadline"
                               step="60" />
                    </div>

                    <!-- Link to Announcement -->
                    <div class="mb-3">
                        <label asp-for="LinkedAnnouncementID"
                               class="form-label fw-semibold">
                            Link to Announcement
                            <span class="text-muted fw-normal">(optional)</span>
                        </label>
                        <select asp-for="LinkedAnnouncementID"
                                class="form-select">
                            <option value="">
                                — No linked announcement —
                            </option>
                            @foreach (var ann in Model.Announcements)
                            {
                                <option value="@ann.AnnouncementID">
                                    @ann.Title
                                </option>
                            }
                        </select>
                    </div>

                </div>
            </div>

            <!-- Submit -->
            <div class="d-grid gap-2">
                <button type="submit"
                        class="btn btn-primary btn-lg">
                    <i class="bi bi-check-circle me-2"></i>
                    Save Changes
                </button>
                <a href="/Event/Details/@Model.EventID"
                   class="btn btn-outline-secondary">
                    Cancel
                </a>
            </div>

        </div>
    </div>
</form>

@section Scripts {
    <script>
        function previewPhoto(input) {
            const preview = document.getElementById('photoPreview');
            const img = document.getElementById('previewImg');
            if (input.files && input.files[0]) {
                const file = input.files[0];
                const allowed = ['image/jpeg', 'image/png', 'image/gif', 'image/webp'];
                if (!allowed.includes(file.type)) {
                    alert('Only image files are allowed (JPG, PNG, GIF, WebP).');
                    input.value = '';
                    preview.classList.add('d-none');
                    return;
                }
                const reader = new FileReader();
                reader.onload = e => {
                    img.src = e.target.result;
                    preview.classList.remove('d-none');
                };
                reader.readAsDataURL(file);
            } else {
                preview.classList.add('d-none');
            }
        }

        function toggleRemovePhoto(cb) {
            const thumb = document.getElementById('existingPhotoThumb');
            const newSection = document.getElementById('newPhotoSection');
            if (cb.checked) {
                thumb.style.opacity = '0.3';
                newSection.style.display = 'none';
            } else {
                thumb.style.opacity = '1';
                newSection.style.display = '';
            }
        }

        function toggleOnline() {
            const isOnline = document.getElementById('isOnlineSwitch').checked;
            document.getElementById('meetingUrlDiv').style.display =
                isOnline ? 'block' : 'none';
        }

        document.addEventListener('DOMContentLoaded', function () {
            // Show meeting URL if already online
            if (document.getElementById('isOnlineSwitch').checked) {
                document.getElementById('meetingUrlDiv').style.display = 'block';
            }

            function formatDateTimeLocal(date) {
                const year  = date.getFullYear();
                const month = String(date.getMonth() + 1).padStart(2, '0');
                const day   = String(date.getDate()).padStart(2, '0');
                const hours = String(date.getHours()).padStart(2, '0');
                const mins  = String(date.getMinutes()).padStart(2, '0');
                return `${year}-${month}-${day}T${hours}:${mins}`;
            }

            var startField  = document.getElementById('startDateTime');
            var endField    = document.getElementById('endDateTime');
            var regDeadline = document.getElementById('regDeadline');

            var todayMidnight = new Date();
            todayMidnight.setHours(0, 0, 0, 0);
            var todayStr = formatDateTimeLocal(todayMidnight);

            // Only grey out past days, don't force start > now
            // (event may already be ongoing when editing)
            if (startField) startField.min = todayStr;
            if (endField)   endField.min   =
                (startField && startField.value) ? startField.value : todayStr;
            if (regDeadline) regDeadline.min = todayStr;

            if (startField) {
                startField.addEventListener('change', function () {
                    if (endField) endField.min = this.value || todayStr;
                    if (regDeadline) regDeadline.max = this.value;
                });
            }
        });
    </script>
}
```

- [ ] **Step 2: Build**

```powershell
dotnet build EduConnect.Web
```

Expected: `Build succeeded.`

- [ ] **Step 3: Commit**

```powershell
git add EduConnect.Web/Views/Event/Edit.cshtml
git commit -m "feat: add Event Edit view with existing photo management"
```

---

## Task 6: Update `Details.cshtml` — Delete button + modal, fix Edit gate

**Files:**
- Modify: `EduConnect.Web/Views/Event/Details.cshtml`

- [ ] **Step 1: Add `isCreator` bool at the top of the view**

Find the `@{ ... }` block at the top of `Details.cshtml`:

```cshtml
@{
    ViewData["Title"] = Model.EventTitle;
    var roleName = Context.Session
        .GetString("RoleName");
    bool isOrganizer = (bool)ViewBag.IsOrganizer;
}
```

Replace it with:

```cshtml
@{
    ViewData["Title"] = Model.EventTitle;
    var roleName = Context.Session
        .GetString("RoleName");
    bool isOrganizer = (bool)ViewBag.IsOrganizer;
    bool isCreator   = (bool)ViewBag.IsCreator;
}
```

- [ ] **Step 2: Update the Organizer Actions card to use `isCreator` for Edit/Delete**

Find the "Organizer Actions" sidebar card:

```cshtml
        <!-- Organizer Actions -->
        @if (isOrganizer)
        {
            <div class="card border-0 shadow-sm">
                <div class="card-header bg-white
                                 border-0 pt-3">
                    <h6 class="fw-bold mb-0">
                        <i class="bi bi-gear
                                       me-2 text-primary"></i>
                        Organizer Actions
                    </h6>
                </div>
                <div class="card-body d-grid gap-2">
                    <a href="/Event/Edit/@Model.EventID"
                       class="btn btn-outline-primary
                                  btn-sm">
                        <i class="bi bi-pencil me-2"></i>
                        Edit Event
                    </a>
                    <a href="/Event/Registrants/@Model.EventID"
                       class="btn btn-primary btn-sm">
                        <i class="bi bi-people-fill
                                       me-2"></i>
                        View Registrants
                    </a>
                </div>
            </div>
        }
```

Replace it with:

```cshtml
        <!-- Organizer Actions -->
        @if (isOrganizer)
        {
            <div class="card border-0 shadow-sm">
                <div class="card-header bg-white
                                 border-0 pt-3">
                    <h6 class="fw-bold mb-0">
                        <i class="bi bi-gear
                                       me-2 text-primary"></i>
                        Organizer Actions
                    </h6>
                </div>
                <div class="card-body d-grid gap-2">
                    @if (isCreator)
                    {
                        <a href="/Event/Edit/@Model.EventID"
                           class="btn btn-outline-primary btn-sm">
                            <i class="bi bi-pencil me-2"></i>
                            Edit Event
                        </a>
                        <button type="button"
                                class="btn btn-outline-danger btn-sm"
                                data-bs-toggle="modal"
                                data-bs-target="#deleteEventModal">
                            <i class="bi bi-trash me-2"></i>
                            Delete Event
                        </button>
                    }
                    <a href="/Event/Registrants/@Model.EventID"
                       class="btn btn-primary btn-sm">
                        <i class="bi bi-people-fill me-2"></i>
                        View Registrants
                    </a>
                </div>
            </div>
        }
```

- [ ] **Step 3: Add the delete confirmation modal**

Find the closing `</div>` of the outer row (just before `@if (Model.MaxAttendees.HasValue)`):

```cshtml
</div>

@if (Model.MaxAttendees.HasValue)
```

Insert the modal between them:

```cshtml
</div>

@if (isCreator)
{
    <!-- Delete Event Modal -->
    <div class="modal fade" id="deleteEventModal"
         tabindex="-1" aria-hidden="true">
        <div class="modal-dialog">
            <div class="modal-content">
                <div class="modal-header border-0">
                    <h5 class="modal-title fw-bold text-danger">
                        <i class="bi bi-exclamation-triangle me-2"></i>
                        Delete Event?
                    </h5>
                    <button type="button" class="btn-close"
                            data-bs-dismiss="modal"></button>
                </div>
                <div class="modal-body">
                    <p>This will <strong>cancel</strong>
                       "@Model.EventTitle" and notify
                       all registered attendees by email.</p>
                    <p class="text-danger small mb-0">
                        This cannot be undone.
                    </p>
                </div>
                <div class="modal-footer border-0">
                    <button type="button"
                            class="btn btn-outline-secondary"
                            data-bs-dismiss="modal">
                        Keep Event
                    </button>
                    <form method="post"
                          action="/Event/Delete/@Model.EventID">
                        @Html.AntiForgeryToken()
                        <button type="submit"
                                class="btn btn-danger">
                            <i class="bi bi-trash me-2"></i>
                            Yes, Cancel Event
                        </button>
                    </form>
                </div>
            </div>
        </div>
    </div>
}

@if (Model.MaxAttendees.HasValue)
```

- [ ] **Step 4: Build**

```powershell
dotnet build EduConnect.Web
```

Expected: `Build succeeded.`

- [ ] **Step 5: Commit**

```powershell
git add EduConnect.Web/Views/Event/Details.cshtml
git commit -m "feat: add Delete button and modal to event Details; gate Edit/Delete by isCreator"
```

---

## Task 7: Update `Index.cshtml` — Edit/Delete buttons on creator's cards

**Files:**
- Modify: `EduConnect.Web/Views/Event/Index.cshtml`

- [ ] **Step 1: Read `ViewBag.CurrentUserID` at the top of the view**

Find the `@{ ... }` block at the top of `Index.cshtml`:

```cshtml
@{
    ViewData["Title"] = "Events";
    var events = ViewBag.Events as
        List<EduConnect.Web.ViewModels
            .EventListViewModel>
        ?? new List<EduConnect.Web.ViewModels
            .EventListViewModel>();
    bool canManage = (bool)ViewBag.CanManage;
    string viewMode = ViewBag.ViewMode ?? "list";
    string filter = ViewBag.Filter ?? "all";
}
```

Replace it with:

```cshtml
@{
    ViewData["Title"] = "Events";
    var events = ViewBag.Events as
        List<EduConnect.Web.ViewModels
            .EventListViewModel>
        ?? new List<EduConnect.Web.ViewModels
            .EventListViewModel>();
    bool canManage = (bool)ViewBag.CanManage;
    string viewMode = ViewBag.ViewMode ?? "list";
    string filter = ViewBag.Filter ?? "all";
    int currentUserID = ViewBag.CurrentUserID is int cuid ? cuid : 0;
}
```

- [ ] **Step 2: Add Edit/Delete buttons to each event card**

Find the card cover photo block inside the `foreach` loop. The card `<div>` starts with:

```cshtml
                    <div class="card border-0
                                            shadow-sm h-100
                                            announcement-card">

                        <!-- Cover Photo -->
```

Add a creator action strip right after the opening card div and before the cover photo:

```cshtml
                    <div class="card border-0
                                            shadow-sm h-100
                                            announcement-card">

                        @if (ev.OrganizerID == currentUserID)
                        {
                            <div class="d-flex justify-content-end
                                        gap-1 p-2 position-absolute
                                        top-0 end-0">
                                <a href="/Event/Edit/@ev.EventID"
                                   class="btn btn-sm btn-light
                                          border shadow-sm"
                                   title="Edit event">
                                    <i class="bi bi-pencil"></i>
                                </a>
                                <button type="button"
                                        class="btn btn-sm btn-light
                                               border shadow-sm text-danger"
                                        title="Delete event"
                                        data-bs-toggle="modal"
                                        data-bs-target="#deleteModal-@ev.EventID">
                                    <i class="bi bi-trash"></i>
                                </button>
                            </div>
                        }

                        <!-- Cover Photo -->
```

Also make the outer card div use `position-relative` so the absolute buttons sit correctly. Find:

```cshtml
                    <div class="card border-0
                                            shadow-sm h-100
                                            announcement-card">
```

Change to:

```cshtml
                    <div class="card border-0
                                            shadow-sm h-100
                                            announcement-card
                                            position-relative">
```

- [ ] **Step 3: Add per-card delete modals after the closing `</div>` of the card grid**

Find the closing of the `@foreach` loop and its enclosing `<div class="row g-3">`:

```cshtml
        <div class="row g-3">
            @foreach (var ev in events)
            {
                ...
            }
        </div>
```

After the `</div>` that closes `<div class="row g-3">`, add:

```cshtml
        @foreach (var ev in events.Where(
                      e => e.OrganizerID == currentUserID))
        {
            <div class="modal fade"
                 id="deleteModal-@ev.EventID"
                 tabindex="-1" aria-hidden="true">
                <div class="modal-dialog">
                    <div class="modal-content">
                        <div class="modal-header border-0">
                            <h5 class="modal-title fw-bold text-danger">
                                <i class="bi bi-exclamation-triangle me-2"></i>
                                Delete Event?
                            </h5>
                            <button type="button" class="btn-close"
                                    data-bs-dismiss="modal"></button>
                        </div>
                        <div class="modal-body">
                            <p>This will <strong>cancel</strong>
                               "@ev.EventTitle" and notify all
                               registered attendees by email.</p>
                            <p class="text-danger small mb-0">
                                This cannot be undone.
                            </p>
                        </div>
                        <div class="modal-footer border-0">
                            <button type="button"
                                    class="btn btn-outline-secondary"
                                    data-bs-dismiss="modal">
                                Keep Event
                            </button>
                            <form method="post"
                                  action="/Event/Delete/@ev.EventID">
                                @Html.AntiForgeryToken()
                                <button type="submit"
                                        class="btn btn-danger">
                                    <i class="bi bi-trash me-2"></i>
                                    Yes, Cancel Event
                                </button>
                            </form>
                        </div>
                    </div>
                </div>
            </div>
        }
```

- [ ] **Step 4: Build**

```powershell
dotnet build EduConnect.Web
```

Expected: `Build succeeded.`

- [ ] **Step 5: Commit**

```powershell
git add EduConnect.Web/Views/Event/Index.cshtml
git commit -m "feat: show Edit/Delete icon buttons on creator's event cards in Index view"
```

---

## Self-Review Checklist (already verified)

- [x] Spec: authorization split (IsCreator vs IsOrganizer) — covered in Tasks 2, 6
- [x] Spec: GET/POST Edit with photo keep/remove/replace — Task 3
- [x] Spec: POST Delete → cancel + email registrants — Task 4
- [x] Spec: `RemoveCoverPhoto` in VM + view — Tasks 1, 5
- [x] Spec: `OrganizerID` in `EventListViewModel` + Index — Tasks 1, 2, 7
- [x] Spec: Delete modal on Details — Task 6
- [x] Spec: Edit/Delete icon buttons + per-card modals on Index — Task 7
- [x] No placeholders — all code is complete
- [x] Type consistency — `EventFormViewModel`, `EventListViewModel`, controller methods all use the same property names throughout
