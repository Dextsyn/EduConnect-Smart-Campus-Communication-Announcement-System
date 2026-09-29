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

# Run the tests (xUnit, in-memory SQLite — no SQL Server needed)
dotnet test EduConnect.Tests
```

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
1. Student registers (choosing college → department → program; program required) → `VerificationStatus = "Pending"`, `RoleID = "Student Pending"`, `IsActive = false`
2. Admin approves → `VerificationStatus = "Verified"`, `RoleID = "Student"`, `IsActive = true`
3. Admin can reject with a reason, or toggle `IsActive` at any time

Named roles (constants in `RoleNames.cs` — never compare a literal): `Administrator`, `Dean`, `Chairperson`, `Faculty`, `Staff`, `Student`, `Student Pending`

### Key Domain Concepts

**Announcements** have two orthogonal status fields:
- `Status`: `Draft` | `Published` (controls visibility)
- `ApprovalStatus`: `Draft` | `PendingChair` | `PendingDean` | `Approved` | `Rejected`. `IApprovalService` routes: a Faculty submission goes to every active Chairperson of the author's department (or, if none, the college's Deans); a Chairperson's approval is final unless they mark it a Dean-level matter, which sends it to the Deans; a Chairperson's own post can require the Dean the same way. Reviewers act only on announcements whose author is in their department (Chairperson) or college (Dean).

**FeedType** on announcements (`Academic`, `Administrative`, etc.) controls which feed tab the announcement appears in.

**Audience.** Announcements target the academic hierarchy through `AnnouncementTargets` (one of College/Department/Program per row) and non-academic audiences through `AnnouncementTags` (`ALL` = School Wide, office tags; users hold tags via `UserDepartments`). `IAudienceService` owns every audience rule: `VisibleTo`/`AddressedTo` (EF expressions used by the announcement list, the student dashboard, the chatbot and the Dean/Faculty dashboards), `GetRecipientIdsAsync` (notifications), and `GetTargetOptionsAsync`/`ValidateTargetsAsync` (Dean → own college, Chairperson → own department, Faculty → own department's programs). `Details` is deliberately unscoped; Explore shows other colleges' non-emergency announcements.

**Academic hierarchy** (`Colleges` > `Departments` > `Programs`, entity `AcademicProgram`) is replacing the academic rows of `DepartmentTags`. Colleges without departments have one `IsImplicit` department that the UI hides. `Users.CollegeID/DepartmentID/ProgramID` hold a user's placement (Dean: college; Chairperson/Faculty: department; Student: program). `DepartmentTags` stays for `ALL` and non-academic offices. Design: `docs/superpowers/specs/2026-09-28-college-hierarchy-and-roles-design.md`.

**Events** are optionally linked to an announcement (`AnnouncementID` nullable). Registration supports a waitlist: when the event is full, users are added to `EventWaitlist` with a position number. Cancellation automatically notifies the first person on the waitlist by email. QR codes for event check-in are generated with QRCoder and uploaded to the Azure Blob Storage container `qrcodes` (see `IBlobStorageService`); `EventRegistration.QRCode` stores the resulting public blob URL, which views render directly in `<img src>`. A Dean manages the events of organizers placed in their college, a Chairperson those in their department (`EventAccess`).

### Services

`IEmailService` / `EmailService` — sends HTML email via Gmail SMTP (MailKit). Credentials are in `appsettings.json` under `EmailSettings`. In Development, SSL cert validation is skipped. Email is fire-and-forget (failures are logged but don't break the request).

`IBlobStorageService` / `BlobStorageService` — uploads a byte array to an Azure Blob Storage container and returns the blob's public URL. Reads the connection string from the `AzureBlobStorage` configuration key; **never hardcode it** — it lives in user-secrets locally and as an App Service application setting in production. Currently used for event QR codes (container `qrcodes`, which has Blob-level public read access). Throws `InvalidOperationException` if the key is missing.

### Database

SQL Server Express. Connection string in `appsettings.json`:
```
Server=localhost\SQLEXPRESS;Database=EduConnectDB;Trusted_Connection=True;TrustServerCertificate=True;
```

All EF model configuration is in `ApplicationDbContext.OnModelCreating`. Composite unique indexes are defined there (e.g., `{EventID, UserID}` on EventRegistration).

Event cover photos and announcement images go into `wwwroot/uploads/<subfolder>/`. QR codes are the exception — they go to Azure Blob Storage (App Service's local disk is not persistent across restarts/redeploys). Max upload size is 10 MB (configured in `Program.cs` via `FormOptions` and Kestrel limits).

### View Layer

Razor views under `Views/<Controller>/`. Role-specific dashboards: `Admin/Index`, `Dean/Index`, `Faculty/Index`. The shared `_Layout.cshtml` drives the nav. ViewModels live in `ViewModel/` (note: some controllers import from `EduConnect.Web.ViewModels` namespace — the folder was renamed but namespace may be inconsistent).

### Hardcoded localhost URLs

Several email bodies contain `https://localhost:7135/...` links (in `AnnouncementController`). These will need updating before any production deployment.
