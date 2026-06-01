# Campus Safety Reporting — Design Spec

**Date:** 2026-06-01
**Project:** EduConnect (Adamson University)
**Status:** Approved

---

## Overview

A Campus Safety Report feature that lets any logged-in user photograph and describe a broken or damaged campus facility (e.g., a broken door, faulty air-conditioning unit), then submit it for Staff to review and resolve. This is scoped to **facility/property damage only**; harassment, suspicious activity, and medical incident types are deferred to a future addition.

---

## Decisions Made

| Question | Decision |
|---|---|
| Anonymous reporting | Users can optionally submit anonymously (name hidden from staff) |
| Who receives notifications | All active Staff users (email + in-app) |
| Staff management | Full panel: view list, update status, add resolution note, filter |
| Who can submit | Any logged-in user (any role) |
| Primary categorisation | Building location (SV / ST / OZ / CS / CT / Other) |
| Issue type field | Not used in this version; deferred with full incident reporting |

---

## Data Layer

### Model: `IncidentReport` (existing, no changes)

```
ReportID          int PK
ReportedByID      int? FK → Users (null if anonymous submission desired in future)
IncidentType      varchar(50)   ← repurposed: stores building code (SV/ST/OZ/CS/CT/Other)
Description       nvarchar(max) required
Location          varchar(255)  specific location detail (e.g. "Room 201", "2nd floor hallway")
PhotoURL          varchar(500)  relative path to uploaded image
Status            varchar(20)   Pending | Investigating | Resolved | Dismissed  (default: Pending)
HandledByID       int? FK → Users
Resolution        nvarchar(max)
IsAnonymous       bit           default false
ReportedAt        datetime      default now
ResolvedAt        datetime?
```

`ReportedByID` is always populated from the session for logged-in users regardless of `IsAnonymous`. The flag only controls whether the reporter's name is shown in staff-facing views. The field remains nullable (`int?`) in the model to support potential future anonymous/guest submissions without a schema change.

### DbSet & EF Configuration
`IncidentReports` DbSet and FK relationships for `ReportedBy` / `HandledBy` are already registered in `ApplicationDbContext`. No migration needed — the table was created in `20260418075832_V2_AddNewFeatures`.

### File Storage
Photos are saved to `wwwroot/uploads/safety-reports/` and the relative URL (`/uploads/safety-reports/<guid>.<ext>`) is stored in `PhotoURL`. Max file size: 5 MB. Allowed extensions: `.jpg`, `.jpeg`, `.png`.

---

## ViewModel

```csharp
// EduConnect.Web.ViewModels.SafetyReportViewModel
public class SafetyReportViewModel
{
    [Required]
    public string Building { get; set; }       // SV | ST | OZ | CS | CT | Other

    public string? SpecificLocation { get; set; }

    [Required]
    public string Description { get; set; }

    public IFormFile? Photo { get; set; }

    public bool IsAnonymous { get; set; }
}
```

A separate `SafetyReportFilterViewModel` carries filter state (Building, Status) for the staff list page.

---

## Controllers

### `SafetyReportController` — Submission (all logged-in users)

| Action | Route | Description |
|---|---|---|
| `GET Index` | `/SafetyReport` | Redirects logged-in users to Submit; non-logged-in to Login |
| `GET Submit` | `/SafetyReport/Submit` | Renders submission form |
| `POST Submit` | `/SafetyReport/Submit` | Validates, saves report, fires notifications, redirects to Confirmation |
| `GET Confirmation` | `/SafetyReport/Confirmation/{id}` | "Your report was submitted" page with report ID and building |

**Auth check:** `HttpContext.Session.GetString("UserID") != null` — redirect to `Account/Login` if not logged in.

**POST Submit logic:**
1. Validate ViewModel (ModelState)
2. If `Photo` provided: validate extension + size; save to `wwwroot/uploads/safety-reports/`
3. Create `IncidentReport` record: `IncidentType = Building`, `ReportedByID = session UserID`, `IsAnonymous` from form
4. Save to DB
5. Fire notifications (see Notifications section) — fire-and-forget, failures logged only
6. Redirect to `Confirmation/{id}`

---

### `StaffController` — Management panel (Staff and Administrator only)

| Action | Route | Description |
|---|---|---|
| `GET Index` | `/Staff` | Report list with filters; fixes the existing broken Staff redirect |
| `GET ReportDetails` | `/Staff/ReportDetails/{id}` | Full report detail + inline status update form |
| `POST UpdateStatus` | `/Staff/UpdateStatus/{id}` | Saves new status, resolution note, handler ID |

**Auth check:** `RoleName == "Staff" || RoleName == "Administrator"` — redirect to `Account/Login` otherwise.

**Index page features:**
- Filter dropdowns: Building (SV / ST / OZ / CS / CT / Other / All) and Status (All / Pending / Investigating / Resolved / Dismissed)
- Sorted: newest first
- Columns: Report ID, Building, Specific Location, Submitted, Status, Reporter (name or "Anonymous")
- Each row links to `ReportDetails/{id}`

**ReportDetails page features:**
- Photo displayed if present
- Full description, building, specific location, submitted timestamp
- Reporter name (or "Anonymous" if `IsAnonymous = true`)
- Current status badge
- Inline form: Status dropdown + Resolution textarea + Save button

**UpdateStatus logic:**
1. Load report by ID (404 if not found)
2. Update `Status`, `Resolution`, `HandledByID = session UserID`
3. If new status is "Resolved" or "Dismissed": set `ResolvedAt = DateTime.Now`
4. Save and redirect back to `ReportDetails/{id}`

---

## Notifications

Fired after a report is saved in `POST /SafetyReport/Submit`.

**Recipients:** All `Users` where `RoleName == "Staff"` and `IsActive == true`.

**In-app notification (per Staff user):**
```
Type:    "SafetyReport"
Channel: "InApp"
Message: "New safety report submitted — [Building]: [SpecificLocation or 'No specific location']"
Link:    "/Staff/ReportDetails/{id}"
```
Reporter name is never included in the notification message (regardless of anonymous flag) to keep messages uniform.

**Email (per Staff user):**
- Subject: `New Safety Report — [Building]`
- Body: Building, specific location, description (full), submitted at timestamp, link to `/Staff/ReportDetails/{id}`
- Reporter name is omitted from email body if `IsAnonymous = true`
- Fire-and-forget via `IEmailService`; failures are logged but do not fail the request

---

## Views

```
Views/
  SafetyReport/
    Submit.cshtml       — submission form
    Confirmation.cshtml — success page after submit
  Staff/
    Index.cshtml        — report list with filters (fixes missing Staff dashboard)
    ReportDetails.cshtml — detail + status update form
```

All views extend `_Layout.cshtml`. Status badges use colour coding: Pending (yellow), Investigating (blue), Resolved (green), Dismissed (grey) — inline styles consistent with existing event/announcement badges.

---

## Navigation (`_Layout.cshtml`)

- **All logged-in users:** Add "Report Safety Issue" link in the nav (visible to all roles)
- **Staff and Administrator only:** Add "Safety Reports" management link in the nav

---

## Out of Scope (Future: Full Incident Reporting)

- Incident types: Harassment, Suspicious Activity, Medical
- Department-based notification routing
- Anonymous reporter contact mechanism
- Report escalation workflow
- Mobile camera integration

---

## File Upload Summary

| Property | Value |
|---|---|
| Upload folder | `wwwroot/uploads/safety-reports/` |
| Max size | 5 MB |
| Allowed extensions | `.jpg`, `.jpeg`, `.png` |
| Filename format | `{Guid}.{ext}` |
| Stored as | Relative URL in `IncidentReport.PhotoURL` |
