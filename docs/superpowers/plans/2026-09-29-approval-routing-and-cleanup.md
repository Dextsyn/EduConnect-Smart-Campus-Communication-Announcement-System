# Approval Routing & Cleanup Implementation Plan (Plan 5 of 5)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Route announcement approval through the hierarchy (Faculty → their department's Chairperson → optionally the college's Dean), let a Chairperson escalate Dean-level matters, reset announcements that were mid-approval when this ships, move event access and organizations onto placement, retire the legacy college tags, and close the minor items earlier reviews deferred.

**Architecture:** A new `ApprovalService` owns routing (`RouteAsync`: who reviews next and at which status) and review scope (`ReviewableBy`: which pending announcements a reviewer may act on); the five approval actions in `AnnouncementController` become thin callers of it plus `IAudienceService`. Controller behaviour is verified with unit tests that use fake notification and email services, so no real email is sent. Data changes ship as small, idempotent migrations keyed on names.

**Tech Stack:** ASP.NET Core 8 MVC, EF Core 8 (SQL Server; SQLite for tests), xUnit, Razor.

**Spec:** `docs/superpowers/specs/2026-09-28-college-hierarchy-and-roles-design.md`

## Global Constraints

- **Routing:** Faculty submission → `PendingChair` to **every** active Chairperson with the author's `DepartmentID`; if none (e.g. implicit departments) → `PendingDean` to every active Dean with the author's `CollegeID`; if none → refuse with a message. Unplaced authors are refused ("ask your administrator to place you").
- **Chairperson approval** is final (`Approved`) unless "Dean-level matter" is ticked → `PendingDean` to the college's Deans (refused if the college has no active Dean). **Chairperson's own post** with "Requires Dean approval" ticked → saved `PendingDean`/`Draft` and sent to the Deans instead of publishing.
- **Review scope:** a Chairperson acts on `PendingChair` announcements whose author's `DepartmentID` equals theirs; a Dean on `PendingDean` whose author's `CollegeID` equals theirs. Nobody else.
- **In-flight reset:** every `PendingChair`/`PendingDean` announcement at deploy time returns to `Draft` and its author gets an in-app notification to resubmit.
- **Events:** a Dean manages events of organizers in their college, a Chairperson of organizers in their department (plus organizers themselves, as now). Department columns show placement (program → department → college → legacy tag → "—").
- **Legacy college tags** (`DepartmentTags` that are some `College.LegacyTagID`) become inactive and cannot be restored from Tags & Offices; rows are kept (no data loss). The user form's tag becomes an optional **Office tag**. `PlacementService.SyncFeedTagAsync` (the Plan 3 bridge) is removed.
- **Organizations** link to a college (`Organizations.CollegeID`, backfilled from the legacy tag); `DepartmentTagID` stays as a legacy column.
- **No real email in tests or browser checks:** never click Submit / Approve / Reject / Add User in the browser; controller tests use `FakeNotificationService`/`FakeEmailService`.
- Role comparisons use `RoleNames.*`. Never push `main`. Stop the dev server before building. Commits: sentence-case imperative, ending `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- Test command: `cd /c/EduConnect/src && dotnet test EduConnect.Tests` (+ `dotnet build EduConnect.Web` for view changes).
- Local logins: `admin@`, `uitest.faculty@`, `uitest.student@educonnect.edu`; Task 2 adds `uitest.chair@` (Chairperson, IT&IS) and `uitest.dean@` (Dean, CCIT) with `uitest.faculty`'s password — record them in the project memory note.

## Review Focus

1. **A Chairperson of another department (or a Dean of another college) posting to Approve/Reject for an announcement they cannot see** — expected: no change. Test: `Approve_ChairOfOtherDepartment_CannotAct` (Task 2).
2. **A Chairperson ticking "Dean-level matter" in a college with no active Dean** — expected: stays `PendingChair`, clear error. Test: `Approve_ChairEscalateWithoutDean_StaysPending` (Task 2).
3. **A department with two Chairpersons** — expected: both are notified and either can act. Test: `Route_FacultyWithChairs_PendingChairToAllActiveChairs` (Task 1).
4. **An admin restoring a legacy college tag from Tags & Offices** — expected: refused with a pointer to Academic Structure. Test: `ToggleDepartment_CollegeTag_Refused` (Task 6).
5. **Running the in-flight reset twice** — expected: the second run changes nothing and sends no second notification. Task 4's sqlcmd check.

---

## File Structure

| File | Responsibility |
|---|---|
| `src/EduConnect.Web/Services/IApprovalService.cs`, `ApprovalService.cs` (create) | Routing and review scope |
| `src/EduConnect.Web/Services/EventAccess.cs` (create) | Dean/Chairperson event-management rule |
| `src/EduConnect.Web/Controllers/AnnouncementController.cs` (modify) | Submit/ReviewQueue/Review/Approve/Reject, Chairperson "requires Dean" on Create |
| `src/EduConnect.Web/Views/Announcement/{Review,Create}.cshtml` (modify) | Escalate checkbox, audience names, Dean toggle |
| `src/EduConnect.Web/ViewModel/AnnouncementViewModel.cs` (modify) | `RequiresDeanApproval` |
| `src/EduConnect.Web/Migrations/*` (create 3) | Reset pending, retire college tags, organization college |
| `src/EduConnect.Web/Controllers/EventController.cs` (modify) | Placement-based access and labels |
| `src/EduConnect.Web/Services/{I,}PlacementService.cs` (modify) | `GetPlacementLabelsAsync`; remove `SyncFeedTagAsync` |
| `src/EduConnect.Web/Controllers/{AdminController,AccountController,OrgController,DeanController,FacultyController}.cs` (modify) | Tag retirement, optional office tag, org college, loose ends |
| `src/EduConnect.Web/Views/Admin/*`, `Views/Org/*` (modify) | Labels and fields |
| `src/EduConnect.Tests/{ControllerFakes,ApprovalServiceTests,AnnouncementApprovalTests,EventAccessTests,AdminControllerTests,...}.cs` | Tests |
| `src/CLAUDE.md`, `database/EduConnectDB.sql`, spec (modify) | Docs |

---

### Task 1: Approval routing service

**Files:**
- Create: `src/EduConnect.Web/Services/IApprovalService.cs`, `src/EduConnect.Web/Services/ApprovalService.cs`, `src/EduConnect.Tests/ApprovalServiceTests.cs`
- Modify: `src/EduConnect.Web/Program.cs`

**Interfaces:**
- Consumes: `Viewer` (Plan 4), `RoleNames`.
- Produces:
  - `record ApprovalRouting(bool Ok, string? Error, string Status, List<User> Reviewers)` with `static ApprovalRouting Fail(string error)`
  - `IApprovalService.RouteAsync(int authorId, bool deanOnly) : Task<ApprovalRouting>`
  - `IApprovalService.ReviewableBy(Viewer reviewer) : IQueryable<Announcement>`

- [ ] **Step 1: Write the failing tests**

`src/EduConnect.Tests/ApprovalServiceTests.cs`:
```csharp
using EduConnect.Web;
using EduConnect.Web.Models;
using EduConnect.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace EduConnect.Tests
{
    public class ApprovalServiceTests : IDisposable
    {
        private readonly TestDb _db = new();
        private readonly College _ccit, _law;
        private readonly Department _itis, _cs;
        private readonly User _faculty, _chair, _chair2, _otherChair, _dean;

        public ApprovalServiceTests()
        {
            _ccit = _db.AddCollege("CCIT");
            _itis = _db.AddDepartment(_ccit, "IT&IS");
            _cs = _db.AddDepartment(_ccit, "CS");
            _law = _db.AddCollege("College of Law", flat: true);
            _faculty = _db.AddUser(RoleNames.Faculty, _ccit.CollegeID, _itis.DepartmentID);
            _chair = _db.AddUser(RoleNames.Chairperson, _ccit.CollegeID, _itis.DepartmentID);
            _chair2 = _db.AddUser(RoleNames.Chairperson, _ccit.CollegeID, _itis.DepartmentID);
            _otherChair = _db.AddUser(RoleNames.Chairperson, _ccit.CollegeID, _cs.DepartmentID);
            _dean = _db.AddUser(RoleNames.Dean, _ccit.CollegeID);
        }

        public void Dispose() => _db.Dispose();

        private ApprovalService Service => new(_db.Context);
        private AudienceService Audience => new(_db.Context);

        private static int[] Ids(IEnumerable<User> users) => users.Select(u => u.UserID).OrderBy(x => x).ToArray();

        [Fact]
        public async Task Route_FacultyWithChairs_PendingChairToAllActiveChairs()
        {
            var r = await Service.RouteAsync(_faculty.UserID, deanOnly: false);

            Assert.True(r.Ok);
            Assert.Equal("PendingChair", r.Status);
            Assert.Equal(Ids(new[] { _chair, _chair2 }), Ids(r.Reviewers));
        }

        [Fact]
        public async Task Route_InactiveChairsIgnored_FallsBackToDean()
        {
            foreach (var c in new[] { _chair, _chair2 }) c.IsActive = false;
            _db.Context.SaveChanges();

            var r = await Service.RouteAsync(_faculty.UserID, deanOnly: false);

            Assert.Equal("PendingDean", r.Status);
            Assert.Equal(Ids(new[] { _dean }), Ids(r.Reviewers));
        }

        [Fact]
        public async Task Route_FlatCollegeFaculty_GoesToDean()
        {
            var lawDept = _db.Context.Departments.Single(d => d.CollegeID == _law.CollegeID);
            var lawFaculty = _db.AddUser(RoleNames.Faculty, _law.CollegeID, lawDept.DepartmentID);
            var lawDean = _db.AddUser(RoleNames.Dean, _law.CollegeID);

            var r = await Service.RouteAsync(lawFaculty.UserID, deanOnly: false);

            Assert.Equal("PendingDean", r.Status);
            Assert.Equal(Ids(new[] { lawDean }), Ids(r.Reviewers));
        }

        [Fact]
        public async Task Route_UnplacedAuthor_Fails()
        {
            var unplaced = _db.AddUser(RoleNames.Faculty);

            var r = await Service.RouteAsync(unplaced.UserID, deanOnly: false);

            Assert.False(r.Ok);
            Assert.Contains("administrator", r.Error);
        }

        [Fact]
        public async Task Route_NoChairAndNoDean_Fails()
        {
            var lawDept = _db.Context.Departments.Single(d => d.CollegeID == _law.CollegeID);
            var lawFaculty = _db.AddUser(RoleNames.Faculty, _law.CollegeID, lawDept.DepartmentID);

            Assert.False((await Service.RouteAsync(lawFaculty.UserID, deanOnly: false)).Ok);
        }

        [Fact]
        public async Task Route_DeanOnly_SkipsChairs()
        {
            var r = await Service.RouteAsync(_faculty.UserID, deanOnly: true);

            Assert.Equal("PendingDean", r.Status);
            Assert.Equal(Ids(new[] { _dean }), Ids(r.Reviewers));
        }

        [Fact]
        public async Task Route_DeanOnlyWithoutDean_Fails()
        {
            _dean.IsActive = false;
            _db.Context.SaveChanges();

            var r = await Service.RouteAsync(_chair.UserID, deanOnly: true);

            Assert.False(r.Ok);
            Assert.Contains("no Dean", r.Error);
        }

        private Announcement Pending(User author, string status)
        {
            var a = _db.AddAnnouncement(author, $"{status} by {author.LastName}");
            a.ApprovalStatus = status;
            a.Status = "Draft";
            _db.Context.SaveChanges();
            return a;
        }

        private async Task<int[]> Reviewable(User reviewer)
        {
            var viewer = await Audience.GetViewerAsync(reviewer.UserID);
            return await Service.ReviewableBy(viewer).Select(a => a.AnnouncementID).OrderBy(x => x).ToArrayAsync();
        }

        [Fact]
        public async Task ReviewableBy_Chair_OwnDepartmentsPendingChairOnly()
        {
            var mine = Pending(_faculty, "PendingChair");
            Pending(_faculty, "PendingDean");
            var csFaculty = _db.AddUser(RoleNames.Faculty, _ccit.CollegeID, _cs.DepartmentID);
            Pending(csFaculty, "PendingChair");

            Assert.Equal(new[] { mine.AnnouncementID }, await Reviewable(_chair));
        }

        [Fact]
        public async Task ReviewableBy_Dean_CollegesPendingDeanOnly()
        {
            var mine = Pending(_chair, "PendingDean");
            Pending(_faculty, "PendingChair");
            var lawDean = _db.AddUser(RoleNames.Dean, _law.CollegeID);

            Assert.Equal(new[] { mine.AnnouncementID }, await Reviewable(_dean));
            Assert.Empty(await Reviewable(lawDean));
        }

        [Fact]
        public async Task ReviewableBy_Faculty_Nothing()
        {
            Pending(_faculty, "PendingChair");

            Assert.Empty(await Reviewable(_faculty));
        }
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `cd /c/EduConnect/src && dotnet test EduConnect.Tests 2>&1 | grep -E "error CS" | sort -u | head -2`
Expected: `The type or namespace name 'ApprovalService' could not be found`.

- [ ] **Step 3: Implement**

`src/EduConnect.Web/Services/IApprovalService.cs`:
```csharp
using EduConnect.Web.Models;

namespace EduConnect.Web.Services
{
    public record ApprovalRouting(bool Ok, string? Error, string Status, List<User> Reviewers)
    {
        public static ApprovalRouting Fail(string error) => new(false, error, "", new List<User>());
    }

    // Who reviews an announcement next, and which pending announcements a
    // reviewer may act on. Routing follows the author's placement.
    public interface IApprovalService
    {
        // deanOnly: skip the Chairperson (escalation, or a Chairperson's own
        // post). Otherwise: the author's department's Chairpersons, falling
        // back to the college's Deans.
        Task<ApprovalRouting> RouteAsync(int authorId, bool deanOnly);

        // PendingChair in the Chairperson's department, or PendingDean in
        // the Dean's college. Empty for everyone else.
        IQueryable<Announcement> ReviewableBy(Viewer reviewer);
    }
}
```
`src/EduConnect.Web/Services/ApprovalService.cs`:
```csharp
using EduConnect.Web.Data;
using EduConnect.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace EduConnect.Web.Services
{
    public class ApprovalService : IApprovalService
    {
        private const string PendingChair = "PendingChair";
        private const string PendingDean = "PendingDean";

        private readonly ApplicationDbContext _context;

        public ApprovalService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<ApprovalRouting> RouteAsync(int authorId, bool deanOnly)
        {
            var author = await _context.Users
                .Where(u => u.UserID == authorId)
                .Select(u => new { u.CollegeID, u.DepartmentID })
                .FirstOrDefaultAsync();

            if (author?.CollegeID == null)
                return ApprovalRouting.Fail(
                    "You are not placed in a college yet. Ask your administrator to place you.");

            if (!deanOnly)
            {
                if (author.DepartmentID == null)
                    return ApprovalRouting.Fail(
                        "You are not placed in a department yet. Ask your administrator to place you.");

                var chairs = await _context.Users
                    .Where(u => u.IsActive &&
                                u.Role.RoleName == RoleNames.Chairperson &&
                                u.DepartmentID == author.DepartmentID)
                    .ToListAsync();
                if (chairs.Count > 0)
                    return new ApprovalRouting(true, null, PendingChair, chairs);
            }

            var deans = await _context.Users
                .Where(u => u.IsActive &&
                            u.Role.RoleName == RoleNames.Dean &&
                            u.CollegeID == author.CollegeID)
                .ToListAsync();
            if (deans.Count > 0)
                return new ApprovalRouting(true, null, PendingDean, deans);

            return ApprovalRouting.Fail(deanOnly
                ? "Your college has no Dean to send this to. Contact your administrator."
                : "Your department has no Chairperson and your college has no Dean to review this. Contact your administrator.");
        }

        public IQueryable<Announcement> ReviewableBy(Viewer reviewer)
        {
            var departmentId = reviewer.DepartmentID;
            var collegeId = reviewer.CollegeID;

            return reviewer.RoleName switch
            {
                RoleNames.Chairperson => _context.Announcements.Where(a =>
                    a.ApprovalStatus == PendingChair &&
                    departmentId != null && a.Author.DepartmentID == departmentId),
                RoleNames.Dean => _context.Announcements.Where(a =>
                    a.ApprovalStatus == PendingDean &&
                    collegeId != null && a.Author.CollegeID == collegeId),
                _ => _context.Announcements.Where(a => false)
            };
        }
    }
}
```
In `Program.cs`, after the `IAudienceService` registration add:
```csharp
builder.Services.AddScoped<EduConnect.Web.Services.IApprovalService, EduConnect.Web.Services.ApprovalService>();
```

- [ ] **Step 4: Run to verify they pass**

Run: `cd /c/EduConnect/src && dotnet test EduConnect.Tests 2>&1 | tail -1`
Expected: `Passed!  - Failed: 0, Passed: 123`.

- [ ] **Step 5: Commit**

```bash
cd /c/EduConnect && git add src && git commit -m "Route approvals by department and college

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: Review flow on the new routing

**Files:**
- Modify: `src/EduConnect.Web/Controllers/AnnouncementController.cs` (ctor; `Submit`, `ReviewQueue`, `Review`, `Approve`, `Reject`), `src/EduConnect.Web/Views/Announcement/Review.cshtml` (approve form, tag chips)
- Modify: `src/EduConnect.Tests/ControllerFakes.cs`; Create: `src/EduConnect.Tests/AnnouncementApprovalTests.cs`

**Interfaces:**
- Consumes: `IApprovalService` (Task 1), `IAudienceService.GetViewerAsync/HasAudienceAsync/GetAudienceNamesAsync`.
- Produces: `Approve(int id, bool escalateToDean = false)`; private helpers `NotifyReviewersAsync(Announcement, IEnumerable<User>, string message, string subject, string intro)`, `NotifyAuthorAsync(Announcement, string type, string message, string subject, string bodyHtml)`; test fakes `FakeNotificationService` (`Sent : List<(int UserId, string Type)>`), `FakeEmailService` (`Sent : List<string>` of addresses).

- [ ] **Step 1: Fakes**

Append to `ControllerFakes.cs` (inside the namespace):
```csharp
    // Records notifications instead of saving or broadcasting them.
    public sealed class FakeNotificationService : INotificationService
    {
        public List<(int UserId, string Type)> Sent { get; } = new();

        public Task SendAsync(int userId, string type, string message, string? link = null, int? announcementId = null)
        {
            Sent.Add((userId, type));
            return Task.CompletedTask;
        }

        public Task SendToManyAsync(IEnumerable<int> userIds, string type, string message, string? link = null, int? announcementId = null)
        {
            foreach (var id in userIds) Sent.Add((id, type));
            return Task.CompletedTask;
        }
    }

    // Records addresses instead of sending mail.
    public sealed class FakeEmailService : IEmailService
    {
        public List<string> Sent { get; } = new();

        public Task SendEmailAsync(string toEmail, string toName, string subject, string htmlBody)
        {
            Sent.Add(toEmail);
            return Task.CompletedTask;
        }
    }
```

- [ ] **Step 2: Write the failing tests**

`src/EduConnect.Tests/AnnouncementApprovalTests.cs`:
```csharp
using EduConnect.Web;
using EduConnect.Web.Controllers;
using EduConnect.Web.Models;
using EduConnect.Web.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace EduConnect.Tests
{
    public class AnnouncementApprovalTests : IDisposable
    {
        private readonly TestDb _db = new();
        private readonly FakeNotificationService _notes = new();
        private readonly FakeEmailService _mail = new();
        private readonly FakeSession _session = new();
        private readonly College _ccit;
        private readonly Department _itis, _cs;
        private readonly AcademicProgram _bsit;
        private readonly User _faculty, _chair, _otherChair, _dean;
        private readonly Announcement _post;

        public AnnouncementApprovalTests()
        {
            _ccit = _db.AddCollege("CCIT");
            _itis = _db.AddDepartment(_ccit, "IT&IS");
            _cs = _db.AddDepartment(_ccit, "CS");
            _bsit = _db.AddProgram(_itis, "BSIT");
            _faculty = _db.AddUser(RoleNames.Faculty, _ccit.CollegeID, _itis.DepartmentID);
            _chair = _db.AddUser(RoleNames.Chairperson, _ccit.CollegeID, _itis.DepartmentID);
            _otherChair = _db.AddUser(RoleNames.Chairperson, _ccit.CollegeID, _cs.DepartmentID);
            _dean = _db.AddUser(RoleNames.Dean, _ccit.CollegeID);

            _post = _db.AddAnnouncement(_faculty, "For BSIT");
            _post.Status = "Draft";
            _post.ApprovalStatus = "Draft";
            _db.Context.SaveChanges();
            _db.Target(_post, p: _bsit);
        }

        public void Dispose() => _db.Dispose();

        private AnnouncementController As(User user)
        {
            _session.Clear();
            _session.SetString("UserID", user.UserID.ToString());
            _session.SetString("RoleName", _db.Context.Roles.Single(r => r.RoleID == user.RoleID).RoleName);

            var controller = new AnnouncementController(
                _db.Context,
                NullLogger<AnnouncementController>.Instance,
                null!,
                _notes,
                _mail,
                new FakeBlobStorage(),
                new AudienceService(_db.Context),
                new ApprovalService(_db.Context));
            controller.ControllerContext = new ControllerContext
            {
                HttpContext = FakeSession.HttpContextWith(_session)
            };
            controller.TempData = new TempDataDictionary(
                controller.ControllerContext.HttpContext, new NullTempDataProvider());
            return controller;
        }

        private void SetStatus(string status)
        {
            _post.ApprovalStatus = status;
            _db.Context.SaveChanges();
        }

        private string StatusNow() =>
            _db.NewContext().Announcements.Single(a => a.AnnouncementID == _post.AnnouncementID).ApprovalStatus;

        [Fact]
        public async Task Submit_ProgramOnlyDraft_GoesToTheDepartmentsChair()
        {
            await As(_faculty).Submit(_post.AnnouncementID);

            Assert.Equal("PendingChair", StatusNow());
            Assert.Contains(_notes.Sent, n => n.UserId == _chair.UserID);
            Assert.DoesNotContain(_notes.Sent, n => n.UserId == _otherChair.UserID);
        }

        [Fact]
        public async Task Approve_Chair_IsFinalByDefault()
        {
            SetStatus("PendingChair");

            await As(_chair).Approve(_post.AnnouncementID);

            var saved = _db.NewContext().Announcements.Single(a => a.AnnouncementID == _post.AnnouncementID);
            Assert.Equal("Approved", saved.ApprovalStatus);
            Assert.Equal(_chair.UserID, saved.ApprovedByID);
            Assert.Contains(_notes.Sent, n => n.UserId == _faculty.UserID && n.Type == "AnnouncementApproved");
        }

        [Fact]
        public async Task Approve_ChairEscalate_ForwardsToDean()
        {
            SetStatus("PendingChair");

            await As(_chair).Approve(_post.AnnouncementID, escalateToDean: true);

            Assert.Equal("PendingDean", StatusNow());
            Assert.Contains(_notes.Sent, n => n.UserId == _dean.UserID);
        }

        [Fact]
        public async Task Approve_ChairOfOtherDepartment_CannotAct()
        {
            SetStatus("PendingChair");

            await As(_otherChair).Approve(_post.AnnouncementID);

            Assert.Equal("PendingChair", StatusNow());
        }

        [Fact]
        public async Task Approve_ChairEscalateWithoutDean_StaysPending()
        {
            SetStatus("PendingChair");
            _dean.IsActive = false;
            _db.Context.SaveChanges();

            var result = await As(_chair).Approve(_post.AnnouncementID, escalateToDean: true);

            Assert.Equal("PendingChair", StatusNow());
            Assert.Equal("Review", Assert.IsType<RedirectToActionResult>(result).ActionName);
        }

        [Fact]
        public async Task Reject_Dean_RecordsTheReason()
        {
            SetStatus("PendingDean");

            await As(_dean).Reject(_post.AnnouncementID, "Not this week");

            var saved = _db.NewContext().Announcements.Single(a => a.AnnouncementID == _post.AnnouncementID);
            Assert.Equal("Rejected", saved.ApprovalStatus);
            Assert.Equal("Not this week", saved.RejectionReason);
        }
    }
}
```

- [ ] **Step 3: Run to verify they fail**

Run: `cd /c/EduConnect/src && dotnet test EduConnect.Tests 2>&1 | grep -E "error CS" | sort -u | head -3`
Expected: constructor mismatch (`AnnouncementController` does not take 8 arguments) and `Approve` has no `escalateToDean` parameter.

- [ ] **Step 4: Rewrite the five actions**

Add `IApprovalService approval` as the last constructor parameter (field `_approval`). Replace the whole bodies of `Submit`, `ReviewQueue`, `Review`, `Approve` and `Reject` (from each action's `[HttpPost]`/signature through its closing brace; keep the section banner comments) with the following, and add the two helpers after `NotifyAsync`:
```csharp
        private async Task NotifyReviewersAsync(Announcement announcement, IEnumerable<User> reviewers,
            string message, string subject, string intro)
        {
            foreach (var reviewer in reviewers)
            {
                await _notificationService.SendAsync(
                    reviewer.UserID,
                    "AnnouncementReview",
                    message,
                    $"/Announcement/Review/{announcement.AnnouncementID}",
                    announcement.AnnouncementID);

                _ = _emailService.SendEmailAsync(
                    reviewer.Email,
                    $"{reviewer.FirstName} {reviewer.LastName}",
                    subject,
                    $"<p>Hello {reviewer.FirstName},</p>" +
                    $"<p>{intro}: <strong>{announcement.Title}</strong></p>" +
                    $"<p><a href='https://localhost:7135/Announcement/Review/" +
                    $"{announcement.AnnouncementID}'>Click here to review</a></p>");
            }
        }

        private async Task NotifyAuthorAsync(Announcement announcement, string type,
            string message, string subject, string bodyHtml)
        {
            await _notificationService.SendAsync(
                announcement.AuthorID, type, message,
                "/Announcement/MyAnnouncements", announcement.AnnouncementID);

            var author = await _context.Users.FindAsync(announcement.AuthorID);
            if (author != null)
                _ = _emailService.SendEmailAsync(
                    author.Email,
                    $"{author.FirstName} {author.LastName}",
                    subject,
                    $"<p>Hello {author.FirstName},</p>{bodyHtml}");
        }
```
`Submit`:
```csharp
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Submit(int id)
        {
            if (!IsLoggedIn())
                return RedirectToAction("Login", "Account");

            if (!IsFaculty())
                return RedirectToAction("Index");

            var userID = GetUserID();

            var announcement = await _context.Announcements
                .FirstOrDefaultAsync(a =>
                    a.AnnouncementID == id &&
                    a.AuthorID == userID &&
                    (a.ApprovalStatus == "Draft" ||
                     a.ApprovalStatus == "Rejected"));

            if (announcement == null)
                return RedirectToAction("MyAnnouncements");

            // Programs, departments and colleges count as much as tags.
            if (!await _audience.HasAudienceAsync(announcement.AnnouncementID))
            {
                TempData["Error"] = "Please choose an audience before submitting.";
                return RedirectToAction("MyAnnouncements");
            }

            // The department's Chairpersons, or the college's Deans when the
            // department has none.
            var routing = await _approval.RouteAsync(userID, deanOnly: false);
            if (!routing.Ok)
            {
                TempData["Error"] = routing.Error;
                return RedirectToAction("MyAnnouncements");
            }

            // Clear stale data from any prior rejected cycle
            announcement.ChairApprovedByID = null;
            announcement.ChairApprovedAt = null;
            announcement.ChairRejectionReason = null;
            announcement.ApprovedByID = null;
            announcement.ApprovedAt = null;
            announcement.RejectionReason = null;

            announcement.ApprovalStatus = routing.Status;
            announcement.SubmittedAt = DateTime.Now;
            await _context.SaveChangesAsync();

            await NotifyReviewersAsync(announcement, routing.Reviewers,
                $"New announcement pending your review: {announcement.Title}",
                "EduConnect: Announcement Pending Review",
                "A new announcement requires your review");

            TempData["Success"] = "Announcement submitted for review.";
            return RedirectToAction("MyAnnouncements");
        }
```
`ReviewQueue`:
```csharp
        public async Task<IActionResult> ReviewQueue()
        {
            if (!IsLoggedIn())
                return RedirectToAction("Login", "Account");

            var roleName = GetRoleName();
            if (roleName != RoleNames.Chairperson && roleName != RoleNames.Dean)
                return RedirectToAction("Index");

            var viewer = await _audience.GetViewerAsync(GetUserID());

            var announcements = await _approval.ReviewableBy(viewer)
                .OrderBy(a => a.SubmittedAt)
                .Select(a => new
                {
                    a.AnnouncementID,
                    a.Title,
                    a.FeedType,
                    a.SubmittedAt,
                    AuthorName = a.Author.FirstName + " " + a.Author.LastName,
                    CategoryName = a.Category.CategoryName
                })
                .ToListAsync();

            ViewBag.Announcements = announcements;
            ViewBag.Role = roleName;
            return View();
        }
```
`Review`:
```csharp
        public async Task<IActionResult> Review(int id)
        {
            if (!IsLoggedIn())
                return RedirectToAction("Login", "Account");

            var roleName = GetRoleName();
            if (roleName != RoleNames.Chairperson && roleName != RoleNames.Dean)
                return RedirectToAction("Index");

            var viewer = await _audience.GetViewerAsync(GetUserID());

            var announcement = await _approval.ReviewableBy(viewer)
                .Include(a => a.Author)
                    .ThenInclude(u => u.Role)
                .Include(a => a.Category)
                .FirstOrDefaultAsync(a => a.AnnouncementID == id);

            if (announcement == null)
                return RedirectToAction("ReviewQueue");

            ViewBag.Role = roleName;
            ViewBag.AudienceNames = await _audience.GetAudienceNamesAsync(id);

            // Escalation needs an active Dean in the author's college.
            if (roleName == RoleNames.Chairperson)
                ViewBag.CanEscalate =
                    (await _approval.RouteAsync(announcement.AuthorID, deanOnly: true)).Ok;

            return View(announcement);
        }
```
`Approve`:
```csharp
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Approve(int id, bool escalateToDean = false)
        {
            if (!IsLoggedIn())
                return RedirectToAction("Login", "Account");

            var roleName = GetRoleName();
            if (roleName != RoleNames.Chairperson && roleName != RoleNames.Dean)
                return RedirectToAction("Index");

            var userID = GetUserID();
            var viewer = await _audience.GetViewerAsync(userID);

            var announcement = await _approval.ReviewableBy(viewer)
                .FirstOrDefaultAsync(a => a.AnnouncementID == id);

            if (announcement == null)
                return RedirectToAction("ReviewQueue");

            if (roleName == RoleNames.Chairperson)
            {
                announcement.ChairApprovedByID = userID;
                announcement.ChairApprovedAt = DateTime.Now;

                // A Dean-level matter goes on to the Dean; otherwise the
                // Chairperson's approval is final.
                if (escalateToDean)
                {
                    var routing = await _approval.RouteAsync(announcement.AuthorID, deanOnly: true);
                    if (!routing.Ok)
                    {
                        TempData["Error"] = routing.Error;
                        return RedirectToAction("Review", new { id });
                    }

                    announcement.ApprovalStatus = "PendingDean";
                    await _context.SaveChangesAsync();

                    await NotifyReviewersAsync(announcement, routing.Reviewers,
                        $"Announcement forwarded for your review: {announcement.Title}",
                        "EduConnect: Announcement Pending Your Approval",
                        "An announcement approved by the Chairperson now needs your final approval");

                    TempData["Success"] = "Approved and sent to the Dean for final approval.";
                    return RedirectToAction("ReviewQueue");
                }
            }

            announcement.ApprovalStatus = "Approved";
            announcement.ApprovedByID = userID;
            announcement.ApprovedAt = DateTime.Now;
            await _context.SaveChangesAsync();

            var approvedBy = roleName == RoleNames.Chairperson ? "the Chairperson" : "the Dean";
            await NotifyAuthorAsync(announcement,
                "AnnouncementApproved",
                "Your announcement has been approved — you can now publish it",
                "EduConnect: Announcement Approved",
                $"<p>Your announcement <strong>{announcement.Title}</strong> has been approved by {approvedBy}. " +
                "You can now publish it.</p>" +
                "<p><a href='https://localhost:7135/Announcement/MyAnnouncements'>Go to My Announcements</a></p>");

            TempData["Success"] = "Announcement approved. The author has been notified.";
            return RedirectToAction("ReviewQueue");
        }
```
`Reject`:
```csharp
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Reject(int id, string rejectionReason)
        {
            if (!IsLoggedIn())
                return RedirectToAction("Login", "Account");

            var roleName = GetRoleName();
            if (roleName != RoleNames.Chairperson && roleName != RoleNames.Dean)
                return RedirectToAction("Index");

            if (string.IsNullOrWhiteSpace(rejectionReason))
            {
                TempData["Error"] = "A rejection reason is required.";
                return RedirectToAction("Review", new { id });
            }

            var viewer = await _audience.GetViewerAsync(GetUserID());

            var announcement = await _approval.ReviewableBy(viewer)
                .FirstOrDefaultAsync(a => a.AnnouncementID == id);

            if (announcement == null)
                return RedirectToAction("ReviewQueue");

            announcement.ApprovalStatus = "Rejected";
            if (roleName == RoleNames.Chairperson)
                announcement.ChairRejectionReason = rejectionReason;
            else
                announcement.RejectionReason = rejectionReason;
            await _context.SaveChangesAsync();

            var rejectedBy = roleName == RoleNames.Chairperson ? "the Chairperson" : "the Dean";
            await NotifyAuthorAsync(announcement,
                "AnnouncementRejected",
                $"Your announcement was rejected by {rejectedBy}",
                "EduConnect: Announcement Rejected",
                $"<p>Your announcement <strong>{announcement.Title}</strong> was rejected by {rejectedBy}.</p>" +
                $"<p><strong>Reason:</strong> {System.Net.WebUtility.HtmlEncode(rejectionReason)}</p>" +
                "<p><a href='https://localhost:7135/Announcement/MyAnnouncements'>" +
                "Go to My Announcements to revise and resubmit</a></p>");

            TempData["Success"] = "Announcement rejected. The author has been notified.";
            return RedirectToAction("ReviewQueue");
        }
```
Fix the existing `AccountControllerTests` only if its build breaks (it does not construct `AnnouncementController`).

- [ ] **Step 5: Review view**

In `Review.cshtml`, replace the approve `<form …Approve…>…</form>` with:
```cshtml
                <form method="post" action="/Announcement/Approve/@Model.AnnouncementID">
                    @Html.AntiForgeryToken()
                    @if (role == RoleNames.Chairperson)
                    {
                        <div class="form-check mb-3">
                            <input class="form-check-input" type="checkbox" name="escalateToDean" value="true"
                                   id="escalateToDean" @(ViewBag.CanEscalate == true ? "" : "disabled") />
                            <label class="form-check-label small" for="escalateToDean">
                                This is a Dean-level matter — send it to the Dean for final approval
                            </label>
                            @if (ViewBag.CanEscalate != true)
                            {
                                <div class="form-text">Your college has no Dean to send it to.</div>
                            }
                        </div>
                    }
                    <button type="submit" class="btn btn-primary w-100">
                        <i class="bi bi-check2 me-2"></i>Approve
                    </button>
                </form>
```
and replace the tag-chip loop
```cshtml
                    @foreach (var tag in Model.AnnouncementTags)
                    {
                        <span class="ec-chip ec-chip-neutral me-1">
                            @tag.DepartmentTag.ShortName
                        </span>
                    }
```
with
```cshtml
                    @foreach (var name in (ViewBag.AudienceNames as List<string>) ?? new List<string>())
                    {
                        <span class="ec-chip ec-chip-neutral me-1">@name</span>
                    }
```

- [ ] **Step 6: Run the tests and build**

Stop the server; `cd /c/EduConnect/src && dotnet build EduConnect.Web 2>&1 | grep -E " error |Build succeeded" | sort -u && dotnet test EduConnect.Tests 2>&1 | tail -1` → `Build succeeded.`, `Passed!  - Failed: 0, Passed: 129`.

- [ ] **Step 7: Test logins and a view-only browser check**

Create the two test logins without sending any email (copy `uitest.faculty`'s password hash):
```bash
sqlcmd -S 'localhost\SQLEXPRESS' -d EduConnectDB -E -C -b -Q "SET NOCOUNT ON; DECLARE @hash NVARCHAR(512) = (SELECT PasswordHash FROM Users WHERE UserID=17); IF NOT EXISTS (SELECT 1 FROM Users WHERE Email='uitest.chair@educonnect.edu') INSERT INTO Users (FirstName, LastName, Email, PasswordHash, RoleID, IsActive, VerificationStatus, CreatedAt, CollegeID, DepartmentID) VALUES ('UITest','Chair','uitest.chair@educonnect.edu',@hash,(SELECT RoleID FROM Roles WHERE RoleName='Chairperson'),1,'Verified',SYSDATETIME(),(SELECT CollegeID FROM Colleges WHERE ShortName='CCIT'),(SELECT DepartmentID FROM Departments WHERE ShortName='IT&IS')); IF NOT EXISTS (SELECT 1 FROM Users WHERE Email='uitest.dean@educonnect.edu') INSERT INTO Users (FirstName, LastName, Email, PasswordHash, RoleID, IsActive, VerificationStatus, CreatedAt, CollegeID) VALUES ('UITest','Dean','uitest.dean@educonnect.edu',@hash,(SELECT RoleID FROM Roles WHERE RoleName='Dean'),1,'Verified',SYSDATETIME(),(SELECT CollegeID FROM Colleges WHERE ShortName='CCIT'))"
```
Add both to the project memory note next to the other uitest logins.

Set up one pending post: place `uitest.faculty` (17) in CCIT → IT&IS and insert a Draft announcement by 17 titled `Plan5 review test` with a BSIT program target and `ApprovalStatus='PendingChair'`, `SubmittedAt=SYSDATETIME()`.

Start the server. **View only — do not click Approve or Reject:**
1. As `uitest.chair`: `/Announcement/ReviewQueue` lists `Plan5 review test`; `/Announcement/Review/<id>` shows the audience chip `BS in Information Technology` and an enabled "Dean-level matter" checkbox.
2. As `uitest.dean`: the queue does not list it (it is `PendingChair`).
Clean up: delete the test announcement and its target; set user 17 back to unplaced. Stop the server.

- [ ] **Step 8: Commit**

```bash
cd /c/EduConnect && git add src && git commit -m "Review announcements by department, with escalation to the Dean

A Chairperson's approval is final unless they mark it a Dean-level
matter. Every Chairperson of the department is notified, and review
rights follow the author's placement.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 3: Chairperson's own post can require the Dean

**Files:**
- Modify: `src/EduConnect.Web/ViewModel/AnnouncementViewModel.cs`, `src/EduConnect.Web/Controllers/AnnouncementController.cs` (Create POST), `src/EduConnect.Web/Views/Announcement/Create.cshtml`
- Test: `src/EduConnect.Tests/AnnouncementApprovalTests.cs`, `src/EduConnect.Tests/TestDb.cs`

**Interfaces:**
- Produces: `AnnouncementFormViewModel.RequiresDeanApproval` (`bool`); `TestDb.CategoryID()`.

- [ ] **Step 1: Write the failing tests**

In `TestDb`, add:
```csharp
        public int CategoryID()
        {
            var category = Context.AnnouncementCategories.FirstOrDefault();
            if (category == null)
            {
                category = new AnnouncementCategory { CategoryName = "General", ColorHex = "#000000", FeedType = "Academic" };
                Context.AnnouncementCategories.Add(category);
                Context.SaveChanges();
            }
            return category.CategoryID;
        }
```
Append inside `AnnouncementApprovalTests` (add `using EduConnect.Web.ViewModels;`):
```csharp
        private AnnouncementFormViewModel ChairPost() => new()
        {
            Title = "Dean-level notice",
            Body = "Body",
            CategoryID = _db.CategoryID(),
            Priority = 1,
            TargetDepartmentIDs = new List<int> { _itis.DepartmentID },
            RequiresDeanApproval = true
        };

        [Fact]
        public async Task Create_ChairRequiringDean_SavesPendingDeanAndNotifiesDean()
        {
            await As(_chair).Create(ChairPost());

            var saved = _db.NewContext().Announcements.Single(a => a.Title == "Dean-level notice");
            Assert.Equal("PendingDean", saved.ApprovalStatus);
            Assert.Equal("Draft", saved.Status);
            Assert.Contains(_notes.Sent, n => n.UserId == _dean.UserID);
        }

        [Fact]
        public async Task Create_ChairRequiringDeanWithoutDean_IsRefused()
        {
            _dean.IsActive = false;
            _db.Context.SaveChanges();

            var result = await As(_chair).Create(ChairPost());

            Assert.IsType<ViewResult>(result);
            Assert.False(_db.NewContext().Announcements.Any(a => a.Title == "Dean-level notice"));
        }
```

- [ ] **Step 2: Run to verify they fail**

Run: `cd /c/EduConnect/src && dotnet test EduConnect.Tests 2>&1 | grep -E "error CS" | sort -u | head -2`
Expected: `'AnnouncementFormViewModel' does not contain a definition for 'RequiresDeanApproval'`.

- [ ] **Step 3: Implement**

In `AnnouncementFormViewModel`, after `IsEmergency` add:
```csharp

        // Chairperson only: a Dean-level matter goes to the college's Deans
        // for final approval instead of being published.
        public bool RequiresDeanApproval { get; set; }
```
In Create POST, immediately after the "Choose at least one audience" check (before `if (!ModelState.IsValid)`), add:
```csharp
            ApprovalRouting? deanRouting = null;
            if (GetRoleName() == RoleNames.Chairperson && model.RequiresDeanApproval)
            {
                deanRouting = await _approval.RouteAsync(userID, deanOnly: true);
                if (!deanRouting.Ok)
                    ModelState.AddModelError("RequiresDeanApproval", deanRouting.Error!);
            }
```
In the status branch, insert a case after `if (IsFaculty()) { … }`:
```csharp
            else if (deanRouting != null && deanRouting.Ok)
            {
                approvalStatus = "PendingDean";
                status = "Draft";
                publishedAt = null;
            }
```
In the `new Announcement { … }` initializer add `SubmittedAt = deanRouting != null && deanRouting.Ok ? DateTime.Now : null,`. After `if (announcement.Status == "Published") await NotifyAsync(announcement, userID);` add:
```csharp
            if (announcement.ApprovalStatus == "PendingDean")
            {
                await NotifyReviewersAsync(announcement, deanRouting!.Reviewers,
                    $"Announcement pending your approval: {announcement.Title}",
                    "EduConnect: Announcement Pending Your Approval",
                    "A Chairperson has sent you an announcement for final approval");
                TempData["Success"] = "Sent to the Dean for final approval.";
                return RedirectToAction("MyAnnouncements");
            }
```

In `Create.cshtml`, right after `@await Html.PartialAsync("_TargetPicker", Model)` add:
```cshtml
                    @if (roleName == RoleNames.Chairperson)
                    {
                        <div class="form-check mt-3">
                            <input class="form-check-input" type="checkbox" name="RequiresDeanApproval" value="true"
                                   id="RequiresDeanApproval" @(Model.RequiresDeanApproval ? "checked" : "") />
                            <label class="form-check-label small" for="RequiresDeanApproval">
                                Dean-level matter — send to the Dean for final approval instead of publishing
                            </label>
                            <span asp-validation-for="RequiresDeanApproval" class="text-danger small d-block"></span>
                        </div>
                    }
```

- [ ] **Step 4: Run to verify they pass**

Run: `cd /c/EduConnect/src && dotnet build EduConnect.Web 2>&1 | grep -E " error |Build succeeded" | sort -u && dotnet test EduConnect.Tests 2>&1 | tail -1` → `Build succeeded.`, `Passed: 131`.

- [ ] **Step 5: View check**

Start the server; as `uitest.chair` open `/Announcement/Create`: the Audience card shows "All of Department of Information Technology & Information Systems", its two programs, and the "Dean-level matter" checkbox. As `uitest.faculty` the checkbox is absent. Do not submit. Stop the server.

- [ ] **Step 6: Commit**

```bash
cd /c/EduConnect && git add src && git commit -m "Let a Chairperson send their own Dean-level post to the Dean

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 4: Return in-flight approvals to draft

**Files:**
- Create: `src/EduConnect.Web/Migrations/<ts>_ResetPendingApprovals.cs` (+ Designer)

- [ ] **Step 1: Baseline**

```bash
sqlcmd -S 'localhost\SQLEXPRESS' -d EduConnectDB -E -C -W -Q "SET NOCOUNT ON; SELECT AnnouncementID, AuthorID, ApprovalStatus FROM Announcements WHERE ApprovalStatus IN ('PendingChair','PendingDean')"
```
Expected locally: one row (announcement 9, `PendingDean`).

- [ ] **Step 2: Migration**

`cd /c/EduConnect/src && dotnet ef migrations add ResetPendingApprovals --project EduConnect.Web` (expect empty `Up`/`Down`), then fill:
```csharp
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Approval now routes by department and college (Plan 5). Anything
            // caught mid-review under the old tag-based routing goes back to
            // Draft, and its author is told to resubmit. Re-running is a no-op:
            // once reset, nothing matches.
            migrationBuilder.Sql(@"
                INSERT INTO Notifications (UserID, AnnouncementID, Type, Message, Link, IsRead, Channel, SentAt)
                SELECT a.AuthorID, a.AnnouncementID, 'AnnouncementReturned',
                       LEFT(N'Your announcement ""' + a.Title + N'"" was returned to draft because the approval process changed. Please submit it again.', 500),
                       '/Announcement/MyAnnouncements', 0, 'InApp', SYSDATETIME()
                FROM Announcements a
                WHERE a.ApprovalStatus IN ('PendingChair', 'PendingDean');

                UPDATE Announcements
                SET ApprovalStatus = 'Draft',
                    SubmittedAt = NULL,
                    ChairApprovedByID = NULL,
                    ChairApprovedAt = NULL,
                    UpdatedAt = SYSDATETIME()
                WHERE ApprovalStatus IN ('PendingChair', 'PendingDean');
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Not reversible: the previous pending state is not recorded.
        }
```

- [ ] **Step 3: Apply and assert (Review Focus 5)**

```bash
cd /c/EduConnect/src && dotnet build EduConnect.Web 2>&1 | grep -E " error |Build succeeded" | sort -u && dotnet ef database update --project EduConnect.Web
sqlcmd -S 'localhost\SQLEXPRESS' -d EduConnectDB -E -C -W -Q "SET NOCOUNT ON; SELECT (SELECT COUNT(*) FROM Announcements WHERE ApprovalStatus IN ('PendingChair','PendingDean')) pending, (SELECT COUNT(*) FROM Notifications WHERE Type='AnnouncementReturned') returned, (SELECT ApprovalStatus FROM Announcements WHERE AnnouncementID=9) ann9"
```
Expected: `pending 0`, `returned 1`, `ann9 Draft`. Re-run the migration's SQL by hand (copy both statements into a scratchpad `.sql` file and run with `sqlcmd -b -i`) and re-query → still `returned 1`.

- [ ] **Step 4: Commit**

```bash
cd /c/EduConnect && git add src && git commit -m "Return announcements caught mid-approval to draft

Their authors are notified to resubmit under the new routing.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 5: Events on placement

**Files:**
- Create: `src/EduConnect.Web/Services/EventAccess.cs`, `src/EduConnect.Tests/EventAccessTests.cs`
- Modify: `src/EduConnect.Web/Services/IPlacementService.cs`, `PlacementService.cs`, `src/EduConnect.Web/Controllers/EventController.cs`
- Test: `src/EduConnect.Tests/PlacementServiceTests.cs`

**Interfaces:**
- Produces: `static EventAccess.ManagesEventsOf(string? role, int? myCollegeId, int? myDepartmentId, int? organizerCollegeId, int? organizerDepartmentId) : bool`; `IPlacementService.GetPlacementLabelsAsync(IEnumerable<int> userIds) : Task<Dictionary<int, string>>`.

- [ ] **Step 1: Write the failing tests**

`src/EduConnect.Tests/EventAccessTests.cs`:
```csharp
using EduConnect.Web;
using EduConnect.Web.Services;

namespace EduConnect.Tests
{
    public class EventAccessTests
    {
        [Fact] public void Dean_SameCollege_True() => Assert.True(EventAccess.ManagesEventsOf(RoleNames.Dean, 1, null, 1, 5));
        [Fact] public void Dean_OtherCollege_False() => Assert.False(EventAccess.ManagesEventsOf(RoleNames.Dean, 1, null, 2, 7));
        [Fact] public void Chair_SameDepartment_True() => Assert.True(EventAccess.ManagesEventsOf(RoleNames.Chairperson, 1, 5, 1, 5));
        [Fact] public void Chair_OtherDepartmentSameCollege_False() => Assert.False(EventAccess.ManagesEventsOf(RoleNames.Chairperson, 1, 5, 1, 6));
        [Fact] public void Faculty_Never() => Assert.False(EventAccess.ManagesEventsOf(RoleNames.Faculty, 1, 5, 1, 5));
        [Fact] public void UnplacedDean_Never() => Assert.False(EventAccess.ManagesEventsOf(RoleNames.Dean, null, null, null, null));
    }
}
```
Append inside `PlacementServiceTests`:
```csharp
        [Fact]
        public async Task Labels_UseTheMostSpecificPlacement()
        {
            _bsit.ShortName = "BSIT";
            _db.Context.SaveChanges();
            var student = _db.AddUser(RoleNames.Student, _ccit.CollegeID, _itis.DepartmentID, _bsit.ProgramID);
            var faculty = _db.AddUser(RoleNames.Faculty, _ccit.CollegeID, _itis.DepartmentID);
            var lawFaculty = _db.AddUser(RoleNames.Faculty, _law.CollegeID, LawDept.DepartmentID);
            var tagged = _db.AddUser(RoleNames.Staff);
            _db.TagUser(tagged, _db.AddTag("REG"));
            var nothing = _db.AddUser(RoleNames.Staff);

            var labels = await Service.GetPlacementLabelsAsync(new[] { student.UserID, faculty.UserID, lawFaculty.UserID, tagged.UserID, nothing.UserID });

            Assert.Equal("BSIT", labels[student.UserID]);
            Assert.Equal("IT&IS", labels[faculty.UserID]);
            Assert.Equal("College of Law", labels[lawFaculty.UserID]);
            Assert.Equal("REG", labels[tagged.UserID]);
            Assert.Equal("—", labels[nothing.UserID]);
        }
```

- [ ] **Step 2: Run to verify they fail**

Run: `cd /c/EduConnect/src && dotnet test EduConnect.Tests 2>&1 | grep -E "error CS" | sort -u | head -2`
Expected: `The name 'EventAccess' does not exist…` / `GetPlacementLabelsAsync` missing.

- [ ] **Step 3: Implement**

`src/EduConnect.Web/Services/EventAccess.cs`:
```csharp
namespace EduConnect.Web.Services
{
    // Besides the organizer, a Dean manages the events of organizers in
    // their college and a Chairperson those of organizers in their
    // department.
    public static class EventAccess
    {
        public static bool ManagesEventsOf(string? role,
            int? myCollegeId, int? myDepartmentId,
            int? organizerCollegeId, int? organizerDepartmentId) =>
            role switch
            {
                RoleNames.Dean => myCollegeId != null && myCollegeId == organizerCollegeId,
                RoleNames.Chairperson => myDepartmentId != null && myDepartmentId == organizerDepartmentId,
                _ => false
            };
    }
}
```
In `IPlacementService` add:
```csharp

        // Short label per user: program → department → college → primary
        // tag → "—". For lists such as event registrants.
        Task<Dictionary<int, string>> GetPlacementLabelsAsync(IEnumerable<int> userIds);
```
In `PlacementService` add:
```csharp
        public async Task<Dictionary<int, string>> GetPlacementLabelsAsync(IEnumerable<int> userIds)
        {
            var ids = userIds.Distinct().ToList();
            var rows = await _context.Users
                .Where(u => ids.Contains(u.UserID))
                .Select(u => new
                {
                    u.UserID,
                    Program = u.AcademicProgram == null ? null
                        : (u.AcademicProgram.ShortName ?? u.AcademicProgram.Name),
                    Department = u.Department == null || u.Department.IsImplicit ? null
                        : (u.Department.ShortName ?? u.Department.Name),
                    College = u.College == null ? null
                        : (u.College.ShortName ?? u.College.Name),
                    Tag = u.UserDepartments
                        .Where(ud => ud.IsPrimary)
                        .Select(ud => ud.DepartmentTag.ShortName)
                        .FirstOrDefault()
                })
                .ToListAsync();

            return rows.ToDictionary(
                r => r.UserID,
                r => r.Program ?? r.Department ?? r.College ?? r.Tag ?? "—");
        }
```

- [ ] **Step 4: Run to verify they pass**

Run: `cd /c/EduConnect/src && dotnet test EduConnect.Tests 2>&1 | tail -1` → `Passed!  - Failed: 0, Passed: 138`.

- [ ] **Step 5: Use them in EventController**

Inject `IPlacementService placement` into `EventController` (field `_placement`).
- `CanScanRegistration`: replace the tag-intersection body after the role check with:
  ```csharp
            var me = await _context.Users
                .Where(u => u.UserID == userID)
                .Select(u => new { u.CollegeID, u.DepartmentID })
                .FirstOrDefaultAsync();
            var organizer = registration.Event.Organizer;

            return me != null && EventAccess.ManagesEventsOf(roleName,
                me.CollegeID, me.DepartmentID,
                organizer.CollegeID, organizer.DepartmentID);
  ```
  and update its comment to "Organizer, or a Dean / Chairperson whose college / department the organizer is placed in."
- `Registrants`: replace the `if (!canAccess && (roleName == RoleNames.Dean || …)) { …tag intersection… }` block with:
  ```csharp
            if (!canAccess)
            {
                var me = await _context.Users
                    .Where(u => u.UserID == userID)
                    .Select(u => new { u.CollegeID, u.DepartmentID })
                    .FirstOrDefaultAsync();
                canAccess = me != null && EventAccess.ManagesEventsOf(roleName,
                    me.CollegeID, me.DepartmentID,
                    ev.Organizer.CollegeID, ev.Organizer.DepartmentID);
            }
  ```
- Department labels: in each place that builds `Department = <user>.UserDepartments.FirstOrDefault(ud => ud.IsPrimary)?.DepartmentTag?.ShortName ?? "—"` (five places: two list projections in `Registrants` for registrations and waitlist, one in the other registrants list, and the two scan-info builders), first compute
  ```csharp
            var labels = await _placement.GetPlacementLabelsAsync(<the user IDs in that list>);
  ```
  before the projection, and replace the expression with `labels.GetValueOrDefault(<user>.UserID, "—")`. For the single-user scan builders use `(await _placement.GetPlacementLabelsAsync(new[] { user.UserID }))[user.UserID]`. Find them with `grep -n "ud => ud.IsPrimary" Controllers/EventController.cs`; afterwards that grep returns nothing.

- [ ] **Step 6: Build and verify**

Stop the server; build + test (`Build succeeded.`, `Passed: 138`). Start the server; as admin open `/Event` and one event's registrants page if any event has registrations (`SELECT TOP 1 EventID FROM EventRegistrations`): the Department column shows program/college labels (e.g. `BSIT`, `CCIT`). Server log clean. Stop the server.

- [ ] **Step 7: Commit**

```bash
cd /c/EduConnect && git add src && git commit -m "Base event access and registrant labels on placement

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 6: Retire the legacy college tags

**Files:**
- Create: `src/EduConnect.Web/Migrations/<ts>_RetireCollegeTags.cs` (+ Designer), `src/EduConnect.Tests/AdminControllerTests.cs`
- Modify: `src/EduConnect.Web/Controllers/AdminController.cs` (`Departments`, `ToggleDepartment`, `AddUser`, `EditUser`), `Views/Admin/{Departments,AddUser,EditUser}.cshtml`, `ViewModel/AdminUserFormViewModel.cs`
- Modify: `src/EduConnect.Web/Services/{IPlacementService,PlacementService}.cs`, `Controllers/AccountController.cs` (remove the bridge), `src/EduConnect.Tests/PlacementServiceTests.cs`

**Interfaces:**
- Produces: `AdminUserFormViewModel.DepartmentTagID` becomes `int?` (optional office tag); `ViewBag.CollegeTagIDs` on Tags & Offices.
- Removes: `IPlacementService.SyncFeedTagAsync` and its five tests (`SyncFeedTag_*`) and the `TagsOf`/`CollegeWithTag` helpers.

- [ ] **Step 1: Write the failing test**

`src/EduConnect.Tests/AdminControllerTests.cs`:
```csharp
using EduConnect.Web;
using EduConnect.Web.Controllers;
using EduConnect.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Logging.Abstractions;

namespace EduConnect.Tests
{
    public class AdminControllerTests : IDisposable
    {
        private readonly TestDb _db = new();
        private readonly FakeSession _session = new();

        public void Dispose() => _db.Dispose();

        private AdminController Controller()
        {
            _session.SetString("RoleName", RoleNames.Administrator);
            var controller = new AdminController(
                _db.Context, new FakeEmailService(), NullLogger<AdminController>.Instance,
                new HierarchyService(_db.Context), new PlacementService(_db.Context));
            controller.ControllerContext = new ControllerContext { HttpContext = FakeSession.HttpContextWith(_session) };
            controller.TempData = new TempDataDictionary(controller.ControllerContext.HttpContext, new NullTempDataProvider());
            return controller;
        }

        [Fact]
        public async Task ToggleDepartment_CollegeTag_Refused()
        {
            var tag = _db.AddTag("CCIT");
            tag.IsActive = false;
            var college = _db.AddCollege("College of Computing and Information Technology");
            college.LegacyTagID = tag.TagID;
            _db.Context.SaveChanges();

            var controller = Controller();
            await controller.ToggleDepartment(tag.TagID);

            Assert.False(_db.NewContext().DepartmentTags.Single(t => t.TagID == tag.TagID).IsActive);
            Assert.Contains("Academic Structure", (string)controller.TempData["Error"]!);
        }
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `cd /c/EduConnect/src && dotnet test EduConnect.Tests 2>&1 | grep -E "  Failed |Passed!|Failed!"`
Expected: `ToggleDepartment_CollegeTag_Refused` FAILS (the tag is restored).

- [ ] **Step 3: Guard and label**

In `AdminController.ToggleDepartment`, right after the `IsSystemDepartment` check add:
```csharp
            // Tags that became colleges live on under Academic Structure;
            // restoring one would bring back the old flat department list.
            if (await _context.Colleges.AnyAsync(c => c.LegacyTagID == dept.TagID))
            {
                TempData["Error"] =
                    $"\"{dept.TagName}\" is now a college. Manage it under Academic Structure.";
                return RedirectToAction("Departments");
            }
```
In `Departments`, before `return View();` add:
```csharp
            ViewBag.CollegeTagIDs = await _context.Colleges
                .Where(c => c.LegacyTagID != null)
                .Select(c => c.LegacyTagID!.Value)
                .ToListAsync();
```
In `Departments.cshtml`, add at the top `var collegeTagIDs = ViewBag.CollegeTagIDs as List<int> ?? new List<int>();`; in the row, after the System badge block, add
```cshtml
                                                @if (collegeTagIDs.Contains(dept.TagID))
                                                {
                                                    <span class="badge bg-light text-dark border ms-1" style="font-size:10px">
                                                        Now a college
                                                    </span>
                                                }
```
and wrap the retire/restore `<form>` condition: change `@if (!isSystem)` to `@if (!isSystem && !collegeTagIDs.Contains(dept.TagID))`.

- [ ] **Step 4: Run to verify it passes**

Run: `cd /c/EduConnect/src && dotnet test EduConnect.Tests 2>&1 | tail -1` → `Passed: 139`.

- [ ] **Step 5: Optional office tag, and remove the bridge**

`AdminUserFormViewModel`: replace
```csharp
        [Required(ErrorMessage = "Feed tag is required")]
        public int DepartmentTagID { get; set; }
```
with
```csharp
        // Optional: School Wide or a non-academic office. Academic users are
        // reached through their placement.
        public int? DepartmentTagID { get; set; }
```
`AdminController.AddUser` POST: wrap the `UserDepartments.Add(new UserDepartment { … TagID = model.DepartmentTagID, … })` and its `SaveChangesAsync` in `if (model.DepartmentTagID.HasValue) { … }` using `model.DepartmentTagID.Value`. `EditUser` POST: replace the whole `// Replace primary department` block with:
```csharp
            // Replace or clear the office tag
            var existingPrimary = user.UserDepartments.FirstOrDefault(ud => ud.IsPrimary);
            if (existingPrimary != null && existingPrimary.TagID != model.DepartmentTagID)
                _context.UserDepartments.Remove(existingPrimary);
            if (model.DepartmentTagID.HasValue &&
                (existingPrimary == null || existingPrimary.TagID != model.DepartmentTagID))
                _context.UserDepartments.Add(new UserDepartment
                {
                    UserID = user.UserID,
                    TagID = model.DepartmentTagID.Value,
                    IsPrimary = true,
                    CreatedAt = DateTime.Now
                });
```
`EditUser` GET: `DepartmentTagID = primaryDept?.TagID,` (no `?? 0`).
Views `AddUser.cshtml` / `EditUser.cshtml`: label `Feed tag` → `Office tag <span class="text-muted fw-normal">(optional)</span>`; form-text → `School Wide or a non-academic office. Students, faculty and deans are reached through their placement.`; in `EditUser.cshtml` add `<option value="">— None —</option>` inside the select; in `AddUser.cshtml` change the placeholder text to `— None —`.

Remove the bridge: delete `SyncFeedTagAsync` from `IPlacementService` and `PlacementService`; in `AccountController.Register` delete
```csharp
            // The feed still reads tags (until Plan 4): give the new student
            // their college's tag.
            await _placement.SyncFeedTagAsync(user);
            await _context.SaveChangesAsync();
```
and in Profile POST change `else await _placement.SyncFeedTagAsync(user);` by deleting the `else` branch. In `PlacementServiceTests` delete the five `SyncFeedTag_*` tests and the `TagsOf` and `CollegeWithTag` helpers.

- [ ] **Step 6: Migration**

`cd /c/EduConnect/src && dotnet ef migrations add RetireCollegeTags --project EduConnect.Web` (expect empty), then:
```csharp
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // College tags are colleges now (Academic Structure). Keep the
            // rows — old announcements and users still reference them — but
            // stop offering them anywhere.
            migrationBuilder.Sql(@"
                UPDATE DepartmentTags
                SET IsActive = 0, UpdatedAt = SYSDATETIME()
                WHERE IsActive = 1
                  AND TagID IN (SELECT LegacyTagID FROM Colleges WHERE LegacyTagID IS NOT NULL);
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                UPDATE DepartmentTags
                SET IsActive = 1, UpdatedAt = SYSDATETIME()
                WHERE TagID IN (SELECT LegacyTagID FROM Colleges WHERE LegacyTagID IS NOT NULL);
            ");
        }
```

- [ ] **Step 7: Build, apply, verify**

Stop the server; build + test (`Build succeeded.`, `Passed: 134` — 139 minus the five removed bridge tests); `dotnet ef database update --project EduConnect.Web`;
```bash
sqlcmd -S 'localhost\SQLEXPRESS' -d EduConnectDB -E -C -W -h -1 -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM DepartmentTags t WHERE t.IsActive=1 AND EXISTS (SELECT 1 FROM Colleges c WHERE c.LegacyTagID=t.TagID)"
```
Expected `0`. Start the server; as admin: Tags & Offices shows the eight college tags as Inactive with "Now a college" and no restore button; `/Admin/EditUser/<a student id>` shows "Office tag (optional)" with only School Wide and offices; as `uitest.student` the dashboard and `/Announcement` still list CCIT announcements. Stop the server.

- [ ] **Step 8: Commit**

```bash
cd /c/EduConnect && git add src && git commit -m "Retire the college tags now that colleges are real

Users keep an optional office tag; academic users are reached through
their placement, so the Plan 3 tag bridge is gone.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 7: Organizations belong to a college

**Files:**
- Modify: `src/EduConnect.Web/Models/Organization.cs`, `Data/ApplicationDbContext.cs`, `ViewModel/OrgViewModel.cs`, `Controllers/OrgController.cs`, `Views/Org/{Create,Edit,Details,Index,Manage}.cshtml`
- Create: `src/EduConnect.Web/Migrations/<ts>_AddOrganizationCollege.cs` (+ Designer, snapshot)

- [ ] **Step 1: Model**

In `Organization`, after `public int? DepartmentTagID { get; set; }` add:
```csharp
        // Legacy: DepartmentTagID pointed at what is now a college. The
        // college is a label only — organizations stay visible to everyone.
        public int? CollegeID { get; set; }
```
and after `public DepartmentTag? DepartmentTag { get; set; }` add `public College? College { get; set; }`. In `ApplicationDbContext`'s `Organization` block add:
```csharp
                entity.HasOne(e => e.College)
                      .WithMany()
                      .HasForeignKey(e => e.CollegeID)
                      .OnDelete(DeleteBehavior.Restrict);
```

- [ ] **Step 2: Migration with backfill**

`dotnet ef migrations add AddOrganizationCollege --project EduConnect.Web` (expect: add column, index, FK only), then append to `Up`:
```csharp
            migrationBuilder.Sql(@"
                UPDATE o SET CollegeID = c.CollegeID
                FROM Organizations o
                JOIN Colleges c ON c.LegacyTagID = o.DepartmentTagID
                WHERE o.CollegeID IS NULL;
            ");
```

- [ ] **Step 3: View model, controller, views**

`OrgViewModel`: rename `DepartmentTagID` → `CollegeID` and `DepartmentOptions` → `CollegeOptions`. `OrgController`:
```bash
cd /c/EduConnect/src/EduConnect.Web && sed -i 's/\.Include(o => o\.DepartmentTag)/.Include(o => o.College)/; s/DepartmentTagID = vm\.DepartmentTagID/CollegeID = vm.CollegeID/; s/vm\.DepartmentTagID = org\.DepartmentTagID/vm.CollegeID = org.CollegeID/; s/org\.DepartmentTagID = vm\.DepartmentTagID/org.CollegeID = vm.CollegeID/' Controllers/OrgController.cs
```
and replace the dropdown block
```csharp
            var depts = await _context.DepartmentTags
                .Where(d => d.IsActive)
                .OrderBy(d => d.TagName)
                .ToListAsync();

            vm.DepartmentOptions = depts
                .Select(d => new SelectListItem(d.TagName, d.TagID.ToString()))
                .ToList();
```
with
```csharp
            vm.CollegeOptions = (await _context.Colleges
                .Where(c => c.IsActive)
                .OrderBy(c => c.Name)
                .ToListAsync())
                .Select(c => new SelectListItem(c.Name, c.CollegeID.ToString()))
                .ToList();
```
Views:
```bash
cd /c/EduConnect/src/EduConnect.Web/Views/Org && sed -i 's/DepartmentTagID/CollegeID/g; s/DepartmentOptions/CollegeOptions/g; s/>\s*Department <span class="text-muted fw-normal">(optional)<\/span>/> College <span class="text-muted fw-normal">(optional)<\/span>/' Create.cshtml Edit.cshtml && sed -i 's/Model\.DepartmentTag\.TagName/Model.College.Name/; s/Model\.DepartmentTag != null/Model.College != null/' Details.cshtml && sed -i 's/group\.Org\.DepartmentTag != null/group.Org.College != null/; s/@group\.Org\.DepartmentTag\.ShortName/@(group.Org.College.ShortName ?? group.Org.College.Name)/' Index.cshtml && sed -i 's/org\.DepartmentTag != null/org.College != null/; s/@org\.DepartmentTag\.ShortName/@(org.College.ShortName ?? org.College.Name)/' Manage.cshtml && grep -n "DepartmentTag\|Department <" *.cshtml
```
Expected final grep: no output (if the label line spans lines and the regex missed it, edit the `Department` label text to `College` by hand in Create/Edit).

- [ ] **Step 4: Build, apply, verify**

Stop the server; build + test (`Passed: 134`); `dotnet ef database update --project EduConnect.Web`;
```bash
sqlcmd -S 'localhost\SQLEXPRESS' -d EduConnectDB -E -C -W -h -1 -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM Organizations WHERE DepartmentTagID IS NOT NULL AND CollegeID IS NULL"
```
Expected `0`. Start the server; as admin `/Org` lists organizations with a college chip where set; `/Org/Create` shows a College dropdown of the nine colleges plus "— University-wide —". Do not submit. Stop the server.

- [ ] **Step 5: Commit**

```bash
cd /c/EduConnect && git add src && git commit -m "Link organizations to a college instead of a tag

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 8: Loose ends from earlier reviews

**Files:**
- Modify: `src/EduConnect.Web/Controllers/AccountController.cs` (Profile POST, `FillPlacementAsync`, Register POST), `Controllers/DeanController.cs`, `Controllers/FacultyController.cs`, `Services/AudienceService.cs`, `Services/ChatbotService.cs`
- Test: `src/EduConnect.Tests/{AccountControllerTests,AudienceServiceTests}.cs`, create `src/EduConnect.Tests/DeanControllerTests.cs`

**Interfaces:**
- Produces: `FillPlacementAsync(ProfileViewModel model, User user, bool? programLocked = null)`.

- [ ] **Step 1: Write the failing tests**

In `AccountControllerTests` append:
```csharp
        [Fact]
        public async Task ProfilePost_NewProgramButOtherErrors_StaysEditable()
        {
            var college = _db.AddCollege("CCIT");
            var dept = _db.AddDepartment(college, "IT&IS");
            var bsit = _db.AddProgram(dept, "BSIT");
            var student = _db.AddUser(RoleNames.Student);
            _session.SetString("UserID", student.UserID.ToString());

            var result = await Controller().Profile(new ProfileViewModel { ProgramID = bsit.ProgramID, Suffix = "Esq." });

            var model = Assert.IsType<ProfileViewModel>(Assert.IsType<ViewResult>(result).Model);
            Assert.True(model.CanEditProgram);
            Assert.Null((await _db.NewContext().Users.SingleAsync()).ProgramID);
        }

        [Fact]
        public async Task Register_WithoutProgram_ReportsItWithTheOtherErrors()
        {
            var controller = Controller();
            controller.ModelState.AddModelError("Password", "Password is required");

            var result = await controller.Register(new RegisterViewModel { Email = "x@test.local" });

            Assert.IsType<ViewResult>(result);
            Assert.True(controller.ModelState.ContainsKey("Placement"));
        }
```
In `AudienceServiceTests` append:
```csharp
        [Fact]
        public async Task Labels_RetiredTargetIsMarked()
        {
            _bsit.IsActive = false;
            _bsit.ShortName = "BSIT";
            _db.Context.SaveChanges();
            var id = await _db.NewContext().Announcements.Where(a => a.Title == "P-BSIT").Select(a => a.AnnouncementID).SingleAsync();
            var rows = new List<AnnouncementTableViewModel> { new() { AnnouncementID = id } };

            await Service.AddTargetLabelsAsync(rows);

            Assert.Equal(new[] { "BSIT (retired)" }, rows[0].Tags);
        }

        [Fact]
        public async Task Validate_UnplacedDean_ExplainsPlacement()
        {
            var unplaced = _db.AddUser(RoleNames.Dean);

            var r = await Service.ValidateTargetsAsync(unplaced.UserID, Sel(c: new[] { _ccit.CollegeID }));

            Assert.Contains("not placed", r.Error);
        }
```
`src/EduConnect.Tests/DeanControllerTests.cs`:
```csharp
using EduConnect.Web;
using EduConnect.Web.Controllers;
using EduConnect.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace EduConnect.Tests
{
    public class DeanControllerTests : IDisposable
    {
        private readonly TestDb _db = new();

        public void Dispose() => _db.Dispose();

        [Fact]
        public async Task Index_AccountDeletedWhileLoggedIn_EndsTheSession()
        {
            var session = new FakeSession();
            session.SetString("UserID", "999");
            session.SetString("RoleName", RoleNames.Dean);
            var controller = new DeanController(_db.Context, NullLogger<DeanController>.Instance, new AudienceService(_db.Context))
            {
                ControllerContext = new ControllerContext { HttpContext = FakeSession.HttpContextWith(session) }
            };

            var result = await controller.Index();

            Assert.IsType<RedirectToActionResult>(result);
            Assert.Empty(session.Keys);
        }
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `cd /c/EduConnect/src && dotnet test EduConnect.Tests 2>&1 | grep -E "  Failed |Passed!|Failed!"`
Expected: the five new tests FAIL (the Dean one with an `InvalidOperationException` from `FirstAsync`).

- [ ] **Step 3: Implement**

a) `AccountController`:
- Change `FillPlacementAsync(ProfileViewModel model, User user)` to `FillPlacementAsync(ProfileViewModel model, User user, bool? programLocked = null)` and its first statement to
  ```csharp
            var locked = programLocked ?? user.ProgramID != null;
            model.CanEditProgram = user.Role.RoleName == RoleNames.Student && !locked;
  ```
- In Profile POST, before the placement block add `var hadProgram = user.ProgramID != null;`; in the invalid branch call `await FillPlacementAsync(model, user, programLocked: hadProgram);` and set `ViewBag.MustChooseProgram = user.Role.RoleName == RoleNames.Student && !hadProgram;`.
- In Register POST, as the first statement add:
  ```csharp
            // Report a missing program together with every other error.
            if (model.ProgramID == null)
                ModelState.AddModelError("Placement", "Choose a program.");
  ```

b) `DeanController.Index` and `FacultyController.Index`: change the placement/scope query's `.FirstAsync()` to `.FirstOrDefaultAsync()` and immediately after it add
  ```csharp
            if (scope == null)            // (placement == null in FacultyController)
            {
                HttpContext.Session.Clear();
                return RedirectToAction("Login", "Account");
            }
  ```
  In `DeanController`, move the `scope` query above the `viewer`/`addressed` lines is not required; keep order.

c) `AudienceService.AddTargetLabelsAsync`: project `Retired = t.ProgramID != null ? !t.AcademicProgram!.IsActive : t.DepartmentID != null ? !t.Department!.IsActive : !t.College!.IsActive` alongside `Label`, and add `label.Retired ? label.Label + " (retired)" : label.Label`. Apply the same to `GetTargetNamesAsync` (project name and retired flag, then format).

d) `AudienceService.ValidateTargetsAsync`: when `options.College == null`, return `HierarchyResult.Fail("You are not placed in a college yet, so you can only post to tags. Ask your administrator to place you.")` instead of the generic message.

e) `ChatbotService`: update the two stale comments that list scoped roles (the `<summary>` of `BuildVisibleAnnouncementsQueryAsync` and the prompt-builder comment) to say everyone except the Administrator is scoped by `IAudienceService.VisibleTo`.

- [ ] **Step 4: Run to verify they pass**

Run: `cd /c/EduConnect/src && dotnet build EduConnect.Web 2>&1 | grep -E " error |Build succeeded" | sort -u && dotnet test EduConnect.Tests 2>&1 | tail -1` → `Build succeeded.`, `Passed!  - Failed: 0, Passed: 139`.

- [ ] **Step 5: Commit**

```bash
cd /c/EduConnect && git add src && git commit -m "Tidy the loose ends from the earlier reviews

A failed profile save no longer shows a newly chosen program as
locked, registration reports a missing program with the other
errors, dashboards end a deleted account's session, retired targets
are labelled, and unplaced authors get a clear message.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 9: Documentation

**Files:**
- Modify: `src/CLAUDE.md`, `database/EduConnectDB.sql`, `docs/superpowers/specs/2026-09-28-college-hierarchy-and-roles-design.md`

- [ ] **Step 1: CLAUDE.md**

Replace the **Announcements** status bullet `ApprovalStatus: Draft | Pending | Approved | Rejected (Dean review workflow)` with:
```markdown
- `ApprovalStatus`: `Draft` | `PendingChair` | `PendingDean` | `Approved` | `Rejected`. `IApprovalService` routes: a Faculty submission goes to every active Chairperson of the author's department (or, if none, the college's Deans); a Chairperson's approval is final unless they mark it a Dean-level matter, which sends it to the Deans; a Chairperson's own post can require the Dean the same way. Reviewers act only on announcements whose author is in their department (Chairperson) or college (Dean).
```
Under **Events** add: `A Dean manages the events of organizers placed in their college, a Chairperson those in their department (EventAccess).` Replace "Hardcoded localhost URLs" paragraph's file list with `AdminController`, `AnnouncementController`, `EventController`.

- [ ] **Step 2: SQL script**

At the very top of `database/EduConnectDB.sql` add:
```sql
-- ============================================================
--  HISTORICAL. This script predates the EF Core migrations and
--  no longer matches the schema (no Colleges/Departments/Programs,
--  old role names). The migrations in src/EduConnect.Web/Migrations
--  are the source of truth: run `dotnet ef database update`.
-- ============================================================
```

- [ ] **Step 3: Spec status**

Change the spec's `Status:` line to `Status: implemented on feature/college-hierarchy (Plans 1–5, 2026-09-28/29).`

- [ ] **Step 4: Commit**

```bash
cd /c/EduConnect && git add src docs database && git commit -m "Document the hierarchy-based approval and audience rules

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

## After this plan

Report: tests passing, the four new migrations for Azure in order (`ResetPendingApprovals`, `RetireCollegeTags`, `AddOrganizationCollege`, after Plans 1–4's), the deploy order (pre-checks, migrations, then push), and that the whole restructure is ready to merge.
