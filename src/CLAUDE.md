# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

```bash
# Run the app (HTTPS on https://localhost:7135)
dotnet run --project EduConnect.Web

# Build
dotnet build EduConnect.Web

# Add a new EF Core migration
dotnet ef migrations add <MigrationName> --project EduConnect.Web

# Apply pending migrations to the database
dotnet ef database update --project EduConnect.Web

# Roll back to a specific migration
dotnet ef database update <MigrationName> --project EduConnect.Web
```

There are no automated tests in this project.

## Architecture

Single ASP.NET Core 8.0 MVC project (`EduConnect.Web`) targeting SQL Server via EF Core. The app is for Adamson University — a campus communication platform.

### Authentication & Authorization

**No ASP.NET Identity.** Auth is entirely session-based with BCrypt password hashing. After login, the session stores: `UserID`, `UserName`, `UserEmail`, `RoleID`, `RoleName`.

Every controller action that requires auth manually checks the session. There are no `[Authorize]` attributes. The pattern used everywhere:

```csharp
private bool IsAdmin() =>
    HttpContext.Session.GetString("RoleName") == "Administrator";
```

`AccountController.RedirectToDashboard()` routes users to their role-specific dashboard after login.

### Roles & User Lifecycle

Roles stored in the `Roles` table. Key flow:
1. Student registers → `VerificationStatus = "Pending"`, `RoleID = "Student Pending"`, `IsActive = false`
2. Admin approves → `VerificationStatus = "Verified"`, `RoleID = "Student"`, `IsActive = true`
3. Admin can reject with a reason, or toggle `IsActive` at any time

Named roles: `Administrator`, `Dean`, `Chair Person`, `Faculty`, `Staff`, `Student`, `Student Pending`

### Key Domain Concepts

**Announcements** have two orthogonal status fields:
- `Status`: `Draft` | `Published` (controls visibility)
- `ApprovalStatus`: `Draft` | `Pending` | `Approved` | `Rejected` (Dean review workflow)

**FeedType** on announcements (`Academic`, `Administrative`, etc.) controls which feed tab the announcement appears in.

**DepartmentTags** are used to target announcements and filter feeds. Users are assigned to departments via the `UserDepartments` junction table (`IsPrimary` flag marks the main department). Announcements tagged with `ShortName = "ALL"` are shown to everyone.

**Events** are optionally linked to an announcement (`AnnouncementID` nullable). Registration supports a waitlist: when the event is full, users are added to `EventWaitlist` with a position number. Cancellation automatically notifies the first person on the waitlist by email. QR codes for event check-in are generated with QRCoder and stored as PNG files under `wwwroot/uploads/qrcodes/`.

### Services

`IEmailService` / `EmailService` — sends HTML email via Gmail SMTP (MailKit). Credentials are in `appsettings.json` under `EmailSettings`. In Development, SSL cert validation is skipped. Email is fire-and-forget (failures are logged but don't break the request).

### Database

SQL Server Express. Connection string in `appsettings.json`:
```
Server=localhost\SQLEXPRESS;Database=EduConnectDB;Trusted_Connection=True;TrustServerCertificate=True;
```

All EF model configuration is in `ApplicationDbContext.OnModelCreating`. Composite unique indexes are defined there (e.g., `{EventID, UserID}` on EventRegistration).

File uploads (event covers, QR codes) go into `wwwroot/uploads/<subfolder>/`. Max upload size is 10 MB (configured in `Program.cs` via `FormOptions` and Kestrel limits).

### View Layer

Razor views under `Views/<Controller>/`. Role-specific dashboards: `Admin/Index`, `Dean/Index`, `Faculty/Index`. The shared `_Layout.cshtml` drives the nav. ViewModels live in `ViewModel/` (note: some controllers import from `EduConnect.Web.ViewModels` namespace — the folder was renamed but namespace may be inconsistent).

### Hardcoded localhost URLs

Several email bodies contain `https://localhost:7135/...` links (in `AdminController`, `EventController`). These will need updating before any production deployment.
