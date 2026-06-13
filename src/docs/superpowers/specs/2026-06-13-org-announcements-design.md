# Organization Announcements Feed — Design Spec

**Date:** 2026-06-13
**Status:** Approved

---

## Overview

Add a dedicated organization announcements feature to EduConnect. Student organizations have an assigned Faculty adviser who posts announcements on behalf of the org. All users can browse a public org feed grouped by organization. Admins create and manage organizations via the admin panel.

---

## Scope

1. EF Core migration to create `Organizations`, `OrgMembers`, `OrgAnnouncements` tables.
2. New `OrgController` with public feed, adviser post creation, and admin org management.
3. Four new Razor views: `Index`, `Details`, `Post`, `Create`/`Edit`, `Manage`.
4. Navbar link for all users; admin sidebar link for org management.

Out of scope: org membership management for students, org events, org approval workflow.

---

## Data Layer

### Models (already exist in code)

**`Organization`** (`Organizations` table)
- `OrgID` PK
- `OrgName` NVARCHAR(200) NOT NULL
- `Description` NVARCHAR(500)
- `LogoURL` NVARCHAR(500)
- `CoverPhotoURL` NVARCHAR(500)
- `DepartmentTagID` INT NULL — NULL means university-wide org
- `CreatedByID` INT NOT NULL → FK to Users
- `IsActive` BIT DEFAULT 1
- `IsVerified` BIT DEFAULT 0
- `CreatedAt`, `UpdatedAt`

**`OrgMember`** (`OrgMembers` table)
- `MemberID` PK
- `OrgID` INT NOT NULL → FK to Organizations
- `UserID` INT NOT NULL → FK to Users
- `OrgRole` NVARCHAR(20) — values: `Member` | `Officer` | `President` | `Adviser`
- `Position` NVARCHAR(100) NULL
- `IsActive` BIT DEFAULT 1
- `JoinedAt`
- Unique index on `{OrgID, UserID}`

**`OrgAnnouncement`** (`OrgAnnouncements` table)
- `OrgAnnouncementID` PK
- `OrgID` INT NOT NULL → FK to Organizations
- `PostedByID` INT NOT NULL → FK to Users
- `Title` NVARCHAR(300) NOT NULL
- `Body` NTEXT NOT NULL
- `AttachmentURL` NVARCHAR(500) NULL
- `IsPinned` BIT DEFAULT 0
- `ExpiresAt` DATETIME NULL
- `PostedAt` DATETIME DEFAULT NOW
- `UpdatedAt`

### ApplicationDbContext changes

Add to `ApplicationDbContext`:
```csharp
public DbSet<Organization> Organizations { get; set; }
public DbSet<OrgMember> OrgMembers { get; set; }
public DbSet<OrgAnnouncement> OrgAnnouncements { get; set; }
```

In `OnModelCreating`, add unique index:
```csharp
modelBuilder.Entity<OrgMember>()
    .HasIndex(m => new { m.OrgID, m.UserID })
    .IsUnique();
```

### Migration

One migration: `AddOrganizationTables` — creates all three tables with FK constraints and the unique index.

---

## OrgController

File: `Controllers/OrgController.cs`

### Permission Helpers

```csharp
private bool IsAdmin() =>
    HttpContext.Session.GetString("RoleName") == "Administrator";

private bool IsLoggedIn() =>
    HttpContext.Session.GetString("UserID") != null;

private int GetUserID() =>
    int.Parse(HttpContext.Session.GetString("UserID"));

// Checks Faculty role AND OrgMembers row with OrgRole = "Adviser" for this org
private async Task<bool> IsAdviserOf(int orgId) { ... }
```

### Routes

| Method | Route | Guard | Purpose |
|--------|-------|-------|---------|
| GET | `/Org` | IsLoggedIn | Public feed grouped by org |
| GET | `/Org/Details/{id}` | IsLoggedIn | Org profile + announcements |
| GET | `/Org/Post/{orgId}` | IsAdviserOf(orgId) | Adviser post form |
| POST | `/Org/Post/{orgId}` | IsAdviserOf(orgId) | Save org announcement |
| GET | `/Org/Manage` | IsAdmin | Admin org list |
| GET | `/Org/Create` | IsAdmin | Create org form |
| POST | `/Org/Create` | IsAdmin | Save org + insert adviser OrgMember row |
| GET | `/Org/Edit/{id}` | IsAdmin | Edit org form |
| POST | `/Org/Edit/{id}` | IsAdmin | Save org changes, update adviser if changed |

### Feed Query (GET /Org)

- Load all active organizations with their `OrgAnnouncements` (ordered: pinned first, then by `PostedAt` desc).
- Optional `?orgId=` query param filters to a single org.
- Expired announcements (`ExpiresAt < DateTime.Now`) are excluded.

### Post Creation (POST /Org/Post/{orgId})

- Validate adviser owns the org via `IsAdviserOf`.
- Save `OrgAnnouncement` with `PostedByID = GetUserID()`, `OrgID = orgId`.
- Handle optional attachment file upload to `wwwroot/uploads/org-attachments/`.
- Redirect to `Details/{orgId}` on success.

### Org Creation (POST /Org/Create)

1. Save `Organization` row with `CreatedByID = GetUserID()`.
2. Insert `OrgMember` row: `OrgID = new org ID`, `UserID = selected AdviserUserID`, `OrgRole = "Adviser"`.
3. Handle optional logo file upload to `wwwroot/uploads/org-logos/`.
4. Redirect to `Manage` on success.

### Org Edit (POST /Org/Edit/{id})

- If adviser changed: find existing `OrgRole = "Adviser"` row for this org and either update it or set `IsActive = false` and insert a new one.
- Save org field updates.

---

## ViewModels

**`OrgFeedViewModel`**
```csharp
public List<OrgFeedGroup> Groups { get; set; }
public int? FilterOrgID { get; set; }
public List<SelectListItem> OrgOptions { get; set; }

public class OrgFeedGroup {
    public Organization Org { get; set; }
    public List<OrgAnnouncement> Announcements { get; set; }
}
```

**`OrgCreateViewModel`**
```csharp
public string OrgName { get; set; }
public string? Description { get; set; }
public IFormFile? Logo { get; set; }
public int? DepartmentTagID { get; set; }
public int AdviserUserID { get; set; }
public List<SelectListItem> FacultyOptions { get; set; }
public List<SelectListItem> DepartmentOptions { get; set; }
```

**`OrgPostViewModel`**
```csharp
public int OrgID { get; set; }
public string OrgName { get; set; }
public string Title { get; set; }
public string Body { get; set; }
public IFormFile? Attachment { get; set; }
public bool IsPinned { get; set; }
public DateTime? ExpiresAt { get; set; }
```

---

## Views

All views under `Views/Org/`.

### `Index.cshtml`

- Filter bar: dropdown of all org names (posts to `?orgId=`), clear button.
- For each org group: card with org logo thumbnail, org name as heading, then a list of its announcements (title, posted date, posted by). Pinned announcements shown first with a pin badge.
- Empty state if no orgs or no posts yet.

### `Details.cshtml`

- Org header: logo (if set), name, description, department badge.
- If current user is adviser for this org: "New Post" button linking to `/Org/Post/{id}`.
- Announcement list: title, body preview, posted-by name, date, pin badge if pinned.

### `Post.cshtml`

- Form fields: Title (text), Body (textarea), Attachment (file, optional), Pin this post (checkbox), Expires At (date input, optional).
- Cancel button back to `Details/{orgId}`.

### `Create.cshtml` / `Edit.cshtml`

- Fields: Org Name, Description (textarea), Logo (file upload), Department (dropdown, optional), Adviser (dropdown of active Faculty users).
- Validation on required fields.

### `Manage.cshtml`

- Table: Org Name | Adviser | Department | Status | Actions.
- Actions: Edit (→ `Edit/{id}`), Deactivate (toggle `IsActive`).
- "Add Organization" button at top → `Create`.

---

## Navigation Changes

**`_Layout.cshtml`** — add after the Events nav link (visible to all logged-in users):
```html
<li class="nav-item">
    <a class="nav-link" href="/Org">
        <i class="bi bi-people"></i> Organizations
    </a>
</li>
```

**Admin sidebar** (`Views/Admin/Index.cshtml` or shared admin partial) — add:
```html
<a href="/Org/Manage" class="...">
    <i class="bi bi-building"></i> Organizations
</a>
```

---

## File Uploads

- Org logos → `wwwroot/uploads/org-logos/`
- Post attachments → `wwwroot/uploads/org-attachments/`
- Max file size: 10 MB (inherits existing Kestrel/FormOptions config)
- Accepted types: images for logos; any file for attachments

---

## Out of Scope

- Student/member self-join to organizations
- Org-level events
- Multiple advisers per org
- Org announcement approval workflow
- Notifications for new org posts
