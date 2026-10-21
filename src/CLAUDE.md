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
1. Student registers (choosing college → department → program; program required) → `VerificationStatus = "EmailPending"`, `RoleID = "Student Pending"`, `IsActive = false`, and a 24h confirmation link is emailed (`IAccountEmailService`; only the token's SHA-256 is stored in `EmailVerificationTokens`). Re-registering over an unconfirmed row whose link expired replaces it.
2. Student clicks the link (`/Account/ConfirmEmail`) → `VerificationStatus = "Pending"`; only now does it reach ITC's queue and notify admins
3. Admin approves → `VerificationStatus = "Verified"`, `RoleID = "Student"`, `IsActive = true`
4. Admin can reject with a reason, or toggle `IsActive` at any time

Status values are constants in `VerificationStatuses.cs`. Accounts created on Admin → Add User have no password field: `IAccountEmailService` generates a 12-character temporary password, emails it, and sets `MustChangePassword` + `TempPasswordExpiresAt` (72h). Login refuses an expired one; otherwise the session flag `PasswordChangeRequired.SessionKey` sends every page load to `/Account/SetPassword` until the user picks their own (middleware in `Program.cs`, runs before the ProgramCompletion one). A password an admin types on Edit User is temporary too. Edit User's "Resend login details" (`AdminController.ResendCredentials`) issues a new one; if the email fails, nothing is saved.

Named roles (constants in `RoleNames.cs` — never compare a literal): `Administrator`, `Dean`, `Chairperson`, `Faculty`, `Staff`, `Student`, `Student Pending`

### Key Domain Concepts

**Announcements** have two orthogonal status fields:
- `Status`: `Draft` | `Published` (controls visibility)
- `ApprovalStatus`: `Draft` | `PendingChair` | `PendingDean` | `Approved` | `Rejected`. `IApprovalService` routes: a Faculty submission goes to every active Chairperson of the author's department (or, if none, the college's Deans); a Chairperson's approval is final unless they mark it a Dean-level matter, which sends it to the Deans; a Chairperson's own post can require the Dean the same way. A post that targets a whole college is always Dean-level (`AudienceService.IsDeanLevel`, worked out from its targets): a Chairperson's goes straight to the Deans, a Faculty member's goes Chair → Dean (the Chair's approval forwards it). Reviewers act only on announcements whose author is in their department (Chairperson) or college (Dean); Approve/Reject record the decision with one conditional UPDATE so two reviewers can't both decide, and push `ReviewQueueChanged` (`INotificationService.SignalAsync`) so open Dean/Chair dashboards refresh their Pending card and review side panel.

**FeedType** on announcements (`Academic`, `Administrative`, etc.) controls which feed tab the announcement appears in.

**Audience.** Announcements target the academic hierarchy through `AnnouncementTargets` (one of College/Department/Program per row) and non-academic audiences through `AnnouncementTags` (users hold tags via `UserDepartments`). The only active tag is `ALL` = School Wide (`AudienceService.SchoolWide`): an announcement carrying it reaches everyone, and holding it is the permission to post one (the "Can post School Wide" checkbox on Add/Edit User). Retired tags (old college tags, the former offices) stay on rows for history but grant nothing. `IAudienceService` owns every audience rule: `VisibleTo`/`AddressedTo` (EF expressions used by the announcement list, the student dashboard, the chatbot and the Dean/Faculty dashboards), `GetRecipientIdsAsync` (notifications), and `GetTargetOptionsAsync`/`ValidateTargetsAsync` (Dean → own college and any of its departments/programs; Chairperson and Faculty → own department and its programs, or their whole college, which makes the post Dean-level). Create pre-ticks a Chairperson's/Faculty member's own department. `Details` is deliberately unscoped; Explore shows other colleges' non-emergency announcements.

**Academic hierarchy** (`Colleges` > `Departments` > `Programs`, entity `AcademicProgram`) is replacing the academic rows of `DepartmentTags`. Colleges without departments have one `IsImplicit` department that the UI hides. `Users.CollegeID/DepartmentID/ProgramID` hold a user's placement (Dean: college; Chairperson/Faculty: department; Student: program). `DepartmentTags` stays only for `ALL`; there is no admin page for tags. Admins move users one at a time on Edit User or in bulk from Manage Users (`AdminController.BulkMove`, which places each user at their role's level); a college/department/program can't be retired while active users are placed in it. Design: `docs/superpowers/specs/2026-09-28-college-hierarchy-and-roles-design.md`.

**Events** are optionally linked to an announcement (`AnnouncementID` nullable). Registration supports a waitlist: when the event is full, users are added to `EventWaitlist` with a position number. Cancellation automatically notifies the first person on the waitlist by email. QR codes for event check-in are generated with QRCoder and uploaded to the Azure Blob Storage container `qrcodes` (see `IBlobStorageService`); `EventRegistration.QRCode` stores the resulting public blob URL, which views render directly in `<img src>`. A Dean manages the events of organizers placed in their college, a Chairperson those in their department (`EventAccess`). An event is for its organizer's college, or (organizer's choice) only their department: `Event.CollegeID/DepartmentID` are stamped from the organizer's placement at creation and never follow them if they move (a Dean's event, or one from a college without departments, is always college-wide). `EventAudience.VisibleTo` is the one rule for every event read (list, calendar, Details, Register, student dashboard, chatbot): outside the audience an event is hidden and Details/Register return 404, except to its organizer, anyone registered or waitlisted, and the Dean/Chairperson who manages it. Administrators and Staff see no events — deliberate, unlike announcements' unscoped `Details`.

**Group Finder** (`GroupController`, chat in `GroupChatHub`): a group's chat expires one week after it is created (`GroupController.ChatLifetime`, stored in `ChatExpiresAt`), Open or Full; joins and leaves never move it. `ChatExpiryService` sweeps hourly (and at startup), deleting the messages and setting `Status = "Dissolved"`; until then, an expired group is hidden from the list, can't be joined, and doesn't count as its creator's one live group.

### Time

The App Service runs with `WEBSITE_TIME_ZONE=Singapore Standard Time` (UTC+8, same as Manila), so `DateTime.Now` is Manila time and that is what the database stores. Event schedules are stored as typed (Manila wall-clock). Exceptions: Group Finder uses `DateTime.UtcNow` throughout (write a UTC value to the browser with `DateTime.SpecifyKind(..., Utc)` so it carries a `Z`), and raw SQL in migrations runs on Azure SQL's UTC clock — use `DATEADD(hour, 8, SYSUTCDATETIME())`, not `SYSDATETIME()`.

### Services

`IAuditService` / `AuditService` — queues an `AuditLogs` row (actor from the session, IP, summary, old/new JSON) on the shared `DbContext`; the caller's next `SaveChangesAsync` writes it with the change. Used for user changes (approve, reject, create, edit, delete, bulk move, a student's first program pick) and Academic Structure changes; shown at `/Admin/AuditLog`. Deleting a user nulls `UserID` on their rows (`ActorName` keeps who it was) instead of deleting them.

`IReportingSanctionService` / `ReportingSanctionService` — false safety reports. Any Staff member can mark a report false (status `False report`, reason required); for a Student reporter (never a legacy anonymous report) that is one strike, and strikes in the last 12 months that weren't revoked set the sanction: 1st warning, 2nd 7-day suspension, 3rd+ 30-day suspension plus an Office of Student Affairs referral flag (OSA is contacted outside the app). `ReportingSanctions` rows are never deleted; a suspension expires by `EndsAt` and is enforced in `SafetyReportController.Submit` (GET and POST); My Reports always stays visible. A student appeals each sanction once; only an Administrator upholds or revokes (`/Staff/Sanctions`), and revoking restores the report's earlier status. Every step is audited under `AuditArea.SafetyReports`.

`IEmailService` / `EmailService` — sends HTML email via Gmail SMTP (MailKit). Credentials are in `appsettings.json` under `EmailSettings`. In Development, SSL cert validation is skipped. Email is fire-and-forget (failures are logged but don't break the request), except that Add User tells the admin when the temporary-password email fails. Links in emails (and event QR codes) come from `AppUrls.BaseUrl` — see "Links in emails" below.

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

### Links in emails

Every absolute link that leaves the app (email buttons, event QR codes) is built from `AppUrls.BaseUrl` (`Services/AppUrls.cs`) — never hardcode a host. It uses the `AppBaseUrl` setting, falling back to the request's scheme and host. `appsettings.json` leaves it empty, `appsettings.Development.json` sets `https://localhost:7135`, and production sets it as the App Service application setting `AppBaseUrl` = `https://educonnect-cnb8auaqg3gqehhx.malaysiawest-01.azurewebsites.net`. A localhost value is ignored when the request came in on a public host, so a dev setting can't put localhost into a production email.
