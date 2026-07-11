# Navbar Cleanup & Student Dashboard Chart Replacement

**Date:** 2026-07-11  
**Status:** Approved

## Summary

Remove clutter from the sidebar navigation per role, strip the Feed toggle (Academic / Org Post) from all roles, and replace the student dashboard announcement charts with an Upcoming Events card.

## Files Affected

- `EduConnect.Web/Views/Shared/_SidebarContent.cshtml`
- `EduConnect.Web/Views/Home/Index.cshtml`
- `EduConnect.Web/ViewModels/DashboardViewModel.cs` (add `UpcomingEventsList`)
- `EduConnect.Web/Controllers/HomeController.cs` (populate `UpcomingEventsList`)

## 1. Feed Toggle — Removed for All Roles

The "Feed" section at the top of the sidebar (the Academic and Org Post buttons + heading + `<hr />`) is removed entirely. It is outside the role `if/else` blocks so the single deletion applies to every authenticated user.

## 2. Sidebar Nav Links per Role

### Administrator
**Remove:** Announcements (`/Announcement`), Events (`/Event`), Organizations (`/Org`)  
**Keep:** Dashboard, Verify Students, Manage Users, Add User

### Dean
**Remove:** Organizations (`/Org`), Group Finder (`/Group`)  
**Keep:** Dashboard, Pending Approvals, New Announcement, Announcements, Events, QR Scanner, Notifications, Report Safety Issue

### Faculty
**Remove:** Group Finder (`/Group`)  
**Keep:** Dashboard, New Announcement, My Announcements, Announcements, Events, Organizations, QR Scanner, Notifications, Report Safety Issue

### Staff
**Remove:** Announcements (`/Announcement`), Events (`/Event`), Organizations (`/Org`), Group Finder (`/Group`)  
**Keep:** Notifications (`/Notification`), Safety Reports (`/Staff`), Report Safety Issue (`/SafetyReport/Submit`)

### Student
No nav link changes. The student sidebar links remain as-is.

### Chair Person
No changes requested; links remain as-is.

## 3. Student Dashboard — Charts Replaced with Upcoming Events

**Remove:** The entire "CHARTS ROW" section (both the line chart and pie chart cards) and the Chart.js inline script block in `@section Scripts`.

**Add:** An Upcoming Events card that lists the next 3 upcoming events with date, title, and a "View" link. This surfaces actionable information students can act on (register for an event), which is more valuable than an abstract announcement count graph. The existing stat card already shows a count; this card makes it meaningful.

### ViewModel change
Add to `DashboardViewModel`:
```csharp
public List<UpcomingEventItem> UpcomingEventsList { get; set; } = new();

public class UpcomingEventItem
{
    public int EventID { get; set; }
    public string Title { get; set; } = "";
    public DateTime StartDate { get; set; }
    public string Location { get; set; } = "";
}
```

### Controller change (`HomeController`)
In the same query block that fetches dashboard data, add:
```csharp
vm.UpcomingEventsList = await _context.Events
    .Where(e => e.StartDate >= DateTime.Now)
    .OrderBy(e => e.StartDate)
    .Take(3)
    .Select(e => new UpcomingEventItem {
        EventID = e.EventID,
        Title = e.Title,
        StartDate = e.StartDate,
        Location = e.Location ?? ""
    })
    .ToListAsync();
```

### View change (`Home/Index.cshtml`)
Replace the charts row with a two-column row:
- **Left (col-12 col-lg-8):** Upcoming Events card with a compact list (date badge + title + location + View button per row). Empty state: "No upcoming events right now."
- **Right (col-12 col-lg-4):** Keep as-is or leave empty / hide if no content is needed there. Option: a "Quick Links" card with 2–3 action buttons (Browse All Events, My Notifications, Report Safety Issue).

The card only renders for students (`@if (isStudentFeed)`). Non-student roles already have their own dashboard pages (`/Admin`, `/Dean`, `/Faculty`) so the `Home/Index.cshtml` charts are effectively student-only anyway.

## Error Handling

No new error paths. The UpcomingEventsList query returns an empty list if no events exist; the view shows an empty-state message.

## Out of Scope

- Bottom tab bar (`_Layout.cshtml`) — not mentioned, no changes.
- Chair Person nav links — not mentioned, no changes.
- Actual access control (route-level authorization) — this task is UI-only (hiding links). Unauthorized route access is a separate concern.
