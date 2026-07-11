# Navbar Cleanup & Student Dashboard Chart Replacement — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove cluttered sidebar nav links per role, strip the Feed toggle from all roles, and replace the student dashboard announcement charts with an Upcoming Events card.

**Architecture:** All sidebar nav is rendered from a single partial `_SidebarContent.cshtml` with an `if/else` block per role. The dashboard is `Views/Home/Index.cshtml` backed by `DashboardViewModel` and `HomeController`. Two tasks: (1) sidebar-only changes, (2) dashboard chart removal + events feature.

**Tech Stack:** ASP.NET Core 8 MVC, Razor views, EF Core (SQL Server), Bootstrap 5.1, Bootstrap Icons, Chart.js (being removed from student view).

## Global Constraints

- No automated tests exist in this project. Verification means: run the app with `dotnet run --project EduConnect.Web`, log in as the relevant role, and visually confirm.
- App runs on `https://localhost:7135`.
- No `[Authorize]` attributes — this task is UI-only (hiding nav links). Route-level access control is out of scope.
- Do not add comments explaining what was removed.
- Do not introduce any new CSS classes or JS beyond what the existing `ec-*` design system provides.

---

## File Map

| File | Change |
|---|---|
| `EduConnect.Web/Views/Shared/_SidebarContent.cshtml` | Remove Feed Toggle section; remove specific nav links from Admin, Dean, Faculty, Staff role blocks |
| `EduConnect.Web/Views/Home/Index.cshtml` | Remove CHARTS ROW + Chart.js script; add Upcoming Events card |
| `EduConnect.Web/ViewModel/DashboardViewModel.cs` | Add `UpcomingEventItem` class + `UpcomingEventsList` property; remove unused graph properties |
| `EduConnect.Web/Controllers/HomeController.cs` | Remove graph data queries; add `UpcomingEventsList` query in student path |

---

## Task 1: Sidebar Cleanup

**Files:**
- Modify: `EduConnect.Web/Views/Shared/_SidebarContent.cshtml`

**Interfaces:**
- Produces: nothing downstream depends on this change

### Step 1.1 — Remove the Feed Toggle section

In `_SidebarContent.cshtml`, delete lines 33–44 plus the `<hr />` immediately after them. That is the entire block starting with `<!-- Feed Toggle -->` through the closing `</div>` and `<hr />`:

```html
<!-- DELETE THIS ENTIRE BLOCK -->
<!-- Feed Toggle -->
<div class="mb-3">
    <div class="ec-side-heading">Feed</div>
    <div class="d-flex gap-2 mt-2 px-2">
        <a href="#" class="btn btn-sm flex-fill btn-warning">
            <i class="bi bi-mortarboard"></i> Academic
        </a>
        <a href="/Org" class="btn btn-sm flex-fill btn-outline-light">
            <i class="bi bi-people"></i> Org Post
        </a>
    </div>
</div>

<hr />
```

The `<hr />` that sits below the Navigation section (after the `</div>` closing the nav links) is separate — leave it in place.

- [ ] Delete the Feed Toggle block and its `<hr />` separator

### Step 1.2 — Remove Admin nav links (Announcements, Events, Organizations)

Inside the `@if (currentRole == "Administrator")` block, delete these three `<a>` tags:

```html
<!-- DELETE -->
<a href="/Announcement" class="ec-side-link">
    <i class="bi bi-megaphone"></i>
    Announcements
</a>
<a href="/Event" class="ec-side-link">
    <i class="bi bi-calendar-event"></i>
    Events
</a>
<a href="/Org" class="ec-side-link">
    <i class="bi bi-people-fill"></i>
    Organizations
</a>
```

The Admin block should end up with exactly four links: Dashboard, Verify Students, Manage Users, Add User.

- [ ] Delete the three Admin nav links

### Step 1.3 — Remove Dean nav links (Organizations, Group Finder)

Inside the `else if (currentRole == "Dean")` block, delete:

```html
<!-- DELETE -->
<a href="/Org" class="ec-side-link">
    <i class="bi bi-people-fill"></i>
    Organizations
</a>
```

and:

```html
<!-- DELETE -->
<a href="/Group"
   class="ec-side-link">
    <i class="bi bi-people"></i>
    Group Finder
</a>
```

- [ ] Delete the two Dean nav links

### Step 1.4 — Remove Faculty nav link (Group Finder)

Inside the `else if (currentRole == "Faculty")` block, delete:

```html
<!-- DELETE -->
<a href="/Group"
   class="ec-side-link">
    <i class="bi bi-people"></i>
    Group Finder
</a>
```

- [ ] Delete the Faculty Group Finder link

### Step 1.5 — Remove Staff nav links (Announcements, Events, Organizations, Group Finder)

Inside the `else if (currentRole == "Staff")` block, delete all four links:

```html
<!-- DELETE -->
<a href="/Announcement"
   class="ec-side-link">
    <i class="bi bi-megaphone"></i>
    Announcements
</a>
<a href="/Event"
   class="ec-side-link">
    <i class="bi bi-calendar-event"></i>
    Events
</a>
<a href="/Org" class="ec-side-link">
    <i class="bi bi-people-fill"></i>
    Organizations
</a>
<a href="/Group"
   class="ec-side-link">
    <i class="bi bi-people"></i>
    Group Finder
</a>
```

The Staff block should end up with exactly three links: Safety Reports (`/Staff`), Report Safety Issue (`/SafetyReport/Submit`), Notifications (`/Notification`).

- [ ] Delete the four Staff nav links

### Step 1.6 — Verify

- [ ] Run the app: `dotnet run --project EduConnect.Web`
- [ ] Log in as **Admin** → sidebar shows: Dashboard, Verify Students, Manage Users, Add User. No Announcements, Events, or Organizations.
- [ ] Log in as **Dean** → sidebar shows no Organizations or Group Finder link.
- [ ] Log in as **Faculty** → sidebar shows no Group Finder link.
- [ ] Log in as **Staff** → sidebar shows only: Safety Reports, Report Safety Issue, Notifications.
- [ ] Any role → no "Feed" section with Academic / Org Post buttons.

### Step 1.7 — Commit

- [ ] `git add EduConnect.Web/Views/Shared/_SidebarContent.cshtml`
- [ ] `git commit -m "feat: remove feed toggle and trim nav links per role"`

---

## Task 2: Replace Dashboard Charts with Upcoming Events Card

**Files:**
- Modify: `EduConnect.Web/ViewModel/DashboardViewModel.cs`
- Modify: `EduConnect.Web/Controllers/HomeController.cs`
- Modify: `EduConnect.Web/Views/Home/Index.cshtml`

**Interfaces:**
- Consumes: `Event` model fields `EventID`, `EventTitle`, `StartDateTime`, `Location`, `IsOnline`, `MeetingURL` (confirmed in `Models/Event.cs`)
- Produces: `DashboardViewModel.UpcomingEventsList` consumed by `Home/Index.cshtml`

### Step 2.1 — Update DashboardViewModel

Open `EduConnect.Web/ViewModel/DashboardViewModel.cs`.

**Delete** the graph data properties (they will have no consumers after this task):

```csharp
// DELETE THIS BLOCK
// ─── Graph Data ─────────────────────────
// Announcements per month (last 6 months)
public List<string> MonthLabels { get; set; }
    = new List<string>();
public List<int> MonthlyCount { get; set; }
    = new List<int>();

// Announcements by category
public List<string> CategoryLabels { get; set; }
    = new List<string>();
public List<int> CategoryCount { get; set; }
    = new List<int>();
```

**Add** the following inside the `DashboardViewModel` class (after the `UpcomingEvents` stat card property):

```csharp
// ─── Upcoming Events (Student dashboard) ─
public List<UpcomingEventItem> UpcomingEventsList { get; set; }
    = new List<UpcomingEventItem>();
```

**Add** the following as a new class at the bottom of the file (inside the same namespace, after `AnnouncementTableViewModel`):

```csharp
public class UpcomingEventItem
{
    public int EventID { get; set; }
    public string EventTitle { get; set; } = "";
    public DateTime StartDateTime { get; set; }
    public string Location { get; set; } = "";
    public bool IsOnline { get; set; }
    public string? MeetingURL { get; set; }
}
```

- [ ] Delete the four graph data properties from `DashboardViewModel`
- [ ] Add `UpcomingEventsList` property to `DashboardViewModel`
- [ ] Add `UpcomingEventItem` class to the file

### Step 2.2 — Update HomeController

Open `EduConnect.Web/Controllers/HomeController.cs`.

**Delete** the entire graph data block (lines 108–143 in the original file — from the `// ─── Graph Data ────────────────────────` comment through the end of `model.CategoryCount = ...`):

```csharp
// DELETE THIS ENTIRE BLOCK
// ─── Graph Data ────────────────────────
// Last 6 months labels
var months = Enumerable.Range(0, 6)
    .Select(i => DateTime.Now.AddMonths(-i))
    .Reverse()
    .ToList();

model.MonthLabels = months
    .Select(m => m.ToString("MMM yyyy"))
    .ToList();

model.MonthlyCount = months
    .Select(m => _context.Announcements
        .Count(a =>
            a.Status == "Published" &&
            a.PublishedAt.HasValue &&
            a.PublishedAt.Value.Month == m.Month &&
            a.PublishedAt.Value.Year == m.Year))
    .ToList();

// Announcements by category
var categoryData = await _context
    .Announcements
    .Where(a => a.Status == "Published")
    .GroupBy(a => a.Category.CategoryName)
    .Select(g => new
    {
        Category = g.Key,
        Count = g.Count()
    })
    .ToListAsync();

model.CategoryLabels = categoryData
    .Select(c => c.Category).ToList();
model.CategoryCount = categoryData
    .Select(c => c.Count).ToList();
```

**Add** the upcoming events query inside the `if (roleName == "Student")` block, immediately before `var feed = await _feedRanking...`. Insert:

```csharp
model.UpcomingEventsList = await _context.Events
    .Where(e => e.StartDateTime >= DateTime.Now
             && e.Status != "Cancelled")
    .OrderBy(e => e.StartDateTime)
    .Take(3)
    .Select(e => new UpcomingEventItem
    {
        EventID = e.EventID,
        EventTitle = e.EventTitle,
        StartDateTime = e.StartDateTime,
        Location = e.Location ?? "",
        IsOnline = e.IsOnline,
        MeetingURL = e.MeetingURL
    })
    .ToListAsync();
```

The `if (roleName == "Student")` block after this change should look like:

```csharp
if (roleName == "Student")
{
    model.UpcomingEventsList = await _context.Events
        .Where(e => e.StartDateTime >= DateTime.Now
                 && e.Status != "Cancelled")
        .OrderBy(e => e.StartDateTime)
        .Take(3)
        .Select(e => new UpcomingEventItem
        {
            EventID = e.EventID,
            EventTitle = e.EventTitle,
            StartDateTime = e.StartDateTime,
            Location = e.Location ?? "",
            IsOnline = e.IsOnline,
            MeetingURL = e.MeetingURL
        })
        .ToListAsync();

    // Personalized feed: 3 sections ranked by behavior
    var userTagIDs = await _context
        .UserDepartments
        .Where(ud => ud.UserID == userID)
        .Select(ud => ud.TagID)
        .ToListAsync();

    var feed = await _feedRanking.GetPersonalizedFeedAsync(
        userID, userTagIDs, searchQuery, filterFeedType);

    model.DepartmentAnnouncements = feed.Department;
    model.ForYouAnnouncements = feed.ForYou;
    model.ExploreAnnouncements = feed.Explore;

    return View(model);
}
```

- [ ] Delete the graph data block from `HomeController`
- [ ] Add the `UpcomingEventsList` query inside the Student branch

### Step 2.3 — Update Home/Index.cshtml — remove charts, add card

Open `EduConnect.Web/Views/Home/Index.cshtml`.

**Delete** the entire CHARTS ROW section (lines 140–173):

```html
<!-- DELETE THIS ENTIRE BLOCK -->
<!-- ─── CHARTS ROW ────────────────────── -->
<div class="row g-3 mb-4">

    <!-- Line Chart — Monthly -->
    <div class="col-12 col-lg-8">
        <div class="card border-0 shadow-sm">
            <div class="card-header bg-white border-0 pt-3">
                <h6 class="fw-bold mb-0">
                    <i class="bi bi-graph-up me-2 text-primary"></i>
                    Announcements — Last 6 Months
                </h6>
            </div>
            <div class="card-body">
                <canvas id="monthlyChart" height="100"></canvas>
            </div>
        </div>
    </div>

    <!-- Pie Chart — By Category -->
    <div class="col-12 col-lg-4">
        <div class="card border-0 shadow-sm">
            <div class="card-header bg-white border-0 pt-3">
                <h6 class="fw-bold mb-0">
                    <i class="bi bi-pie-chart me-2 text-primary"></i>
                    By Category
                </h6>
            </div>
            <div class="card-body">
                <canvas id="categoryChart" height="200"></canvas>
            </div>
        </div>
    </div>

</div>
```

**In its place** (between the stat cards row and the `@{ bool isStudentFeed = ... }` line), insert:

```html
<!-- ─── UPCOMING EVENTS ───────────────── -->
<div class="row g-3 mb-4">
    <div class="col-12">
        <div class="card border-0 shadow-sm">
            <div class="card-header bg-white border-0 pt-3 pb-2 d-flex justify-content-between align-items-center">
                <h6 class="fw-bold mb-0">
                    <i class="bi bi-calendar-event me-2 text-primary"></i>
                    Upcoming Events
                </h6>
                <a href="/Event" class="btn btn-sm btn-outline-primary">See all</a>
            </div>
            <div class="card-body p-0">
                @if (Model.UpcomingEventsList.Count == 0)
                {
                    <div class="ec-empty">
                        <i class="bi bi-calendar-x"></i>
                        No upcoming events right now
                    </div>
                }
                else
                {
                    <ul class="list-group list-group-flush">
                        @foreach (var ev in Model.UpcomingEventsList)
                        {
                            <li class="list-group-item d-flex align-items-center gap-3 py-3">
                                <div class="text-center flex-shrink-0" style="min-width:48px;">
                                    <div class="fw-bold text-primary lh-1 fs-5">@ev.StartDateTime.ToString("dd")</div>
                                    <div class="text-muted small text-uppercase">@ev.StartDateTime.ToString("MMM")</div>
                                </div>
                                <div class="flex-grow-1 overflow-hidden">
                                    <div class="fw-semibold text-truncate">@ev.EventTitle</div>
                                    <small class="text-muted">
                                        @if (ev.IsOnline)
                                        {
                                            <i class="bi bi-camera-video me-1"></i>@:Online
                                        }
                                        else if (!string.IsNullOrEmpty(ev.Location))
                                        {
                                            <i class="bi bi-geo-alt me-1"></i>@ev.Location
                                        }
                                    </small>
                                </div>
                                <a href="/Event/Details/@ev.EventID"
                                   class="btn btn-sm btn-outline-primary flex-shrink-0">
                                    View
                                </a>
                            </li>
                        }
                    </ul>
                }
            </div>
        </div>
    </div>
</div>
```

- [ ] Delete the CHARTS ROW block from `Home/Index.cshtml`
- [ ] Insert the Upcoming Events card in its place

### Step 2.4 — Remove the Chart.js script block

Still in `Home/Index.cshtml`, delete the entire `@section Scripts { ... }` block (the one containing the `monthlyChart` and `categoryChart` script):

```html
<!-- DELETE THIS ENTIRE BLOCK -->
@section Scripts {
    <!-- Chart.js -->
    <script src="https://cdn.jsdelivr.net/npm/chart.js"></script>
    <script>
        // ─── Monthly Line Chart ───────────────
        const monthlyCtx = document
            .getElementById('monthlyChart')
            .getContext('2d');
        // ... (entire chart initialisation code)
    </script>
}
```

Note: The Chart.js CDN `<script>` tag in `_Layout.cshtml` (`<script src="https://cdn.jsdelivr.net/npm/chart.js"></script>`) can be left in place — it is harmless and may be needed by other views later.

- [ ] Delete the `@section Scripts` block from `Home/Index.cshtml`

### Step 2.5 — Build to catch type errors

- [ ] Run: `dotnet build EduConnect.Web`
- [ ] Expected: `Build succeeded` with 0 errors. If the build reports that `MonthLabels`, `MonthlyCount`, `CategoryLabels`, or `CategoryCount` are still referenced somewhere, locate and remove those references.

### Step 2.6 — Verify

- [ ] Run: `dotnet run --project EduConnect.Web`
- [ ] Log in as **Student** → dashboard shows Upcoming Events card where the charts used to be. If events exist in the DB, up to 3 appear with date, title, location/online indicator, and a "View" button. If none exist, the empty-state message shows.
- [ ] Confirm the two chart canvases and Chart.js `new Chart(...)` calls are gone (no JS errors in browser console).
- [ ] Log in as **Admin** (redirects to Admin dashboard) — no regression.
- [ ] Log in as any other role that reaches `Home/Index` (e.g., Chair Person) — Upcoming Events card appears correctly.

### Step 2.7 — Commit

- [ ] `git add EduConnect.Web/ViewModel/DashboardViewModel.cs EduConnect.Web/Controllers/HomeController.cs EduConnect.Web/Views/Home/Index.cshtml`
- [ ] `git commit -m "feat: replace dashboard announcement charts with upcoming events card"`
