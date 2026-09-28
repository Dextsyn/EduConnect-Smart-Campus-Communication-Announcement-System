# Hierarchy Targeting Implementation Plan (Plan 4 of 5)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Announcements target a whole college, departments, or specific programs; everything that decides who sees or is notified about an announcement runs on that hierarchy; and a student's program locks once chosen.

**Architecture:** A new `AnnouncementTargets` table holds hierarchy targets (exactly one of College/Department/Program per row); existing announcements are backfilled with college targets from their legacy college tags. One `AudienceService` owns every audience rule — who may target what, who sees an announcement (an EF `Expression` reused by the announcement list, the student dashboard feed, the chatbot and the Dean/Faculty dashboards), who is notified, and the labels shown on list rows. `DepartmentTags` stay for School Wide (`ALL`) and office tags; tags that have become colleges are no longer offered when posting.

**Tech Stack:** ASP.NET Core 8 MVC, EF Core 8 (SQL Server; SQLite for tests), xUnit, Razor, vanilla JS.

**Spec:** `docs/superpowers/specs/2026-09-28-college-hierarchy-and-roles-design.md`

## Global Constraints

- **Targeting rights:** Dean → own college, any of its departments, any of its programs. Chairperson → own department, any of its programs. Faculty → programs of own department only. Unplaced authors get no hierarchy targets (only the tags they hold). Enforced server-side on Create and Edit.
- **Feed visibility (`VisibleTo`):** an announcement is visible to a user when it is School Wide, OR one of its tags is one of the user's tags, OR the user authored it, OR a target reaches them:
  - Student / Student Pending: a target equal to their program, department or college.
  - Faculty / Chairperson: a target equal to their college or department, or a program target inside their department.
  - Dean: any target inside their college.
  - Administrator: everything (callers skip the filter). Staff and others: tags only.
- **`AddressedTo`** = `VisibleTo` without School Wide and without "authored by me" (the "Your Department" section and dashboard counts).
- **Notifications:** School Wide → every active user; otherwise active users holding one of the tags, or whose `ProgramID` / `DepartmentID` / `CollegeID` equals a program / department / college target. The author is never notified.
- `Details` stays unscoped and Explore keeps showing other colleges' announcements (memory: department-tag semantics). Emergencies stay scoped: Explore still excludes `IsEmergency`.
- **Program lock:** a student may choose a program only while they have none; afterwards only the admin changes it.
- Legacy academic `AnnouncementTags`/`UserDepartments` rows are left in place (Plan 5 retires them). Tags that are some college's `LegacyTagID` are not offered in the post form.
- Events keep their tag-based access (Plan 5).
- Role comparisons use `RoleNames.*`. Never push `main`. Stop the dev server before building. Commits: sentence-case imperative, ending `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- Test command: `cd /c/EduConnect/src && dotnet test EduConnect.Tests` (+ `dotnet build EduConnect.Web` for view changes). Local logins: `admin@`, `uitest.faculty@`, `uitest.student@educonnect.edu` (passwords in project memory).

## Review Focus

1. **A Faculty member tampering the form to target another department's program, a department, or the college** — expected: refused with a message, nothing saved. Tests: `Validate_Faculty*` (Task 4).
2. **A Chairperson or Dean opening Announcements** — expected: only their own department / college (plus School Wide and their own posts), no longer every department. Tests: `VisibleTo_Chairperson_*`, `VisibleTo_Dean_*` (Task 3).
3. **Old announcements tagged with a college tag** — expected: still reach the same people after the backfill (as college targets) and still show their college badge. Task 2's sqlcmd assertion and `VisibleTo_Student_SeesCollegeTarget` (Task 3).
4. **An inactive or pending user, or the author, inside a targeted program** — expected: not notified. Test: `Recipients_SkipInactiveUsersAndAuthor` (Task 5).
5. **A student who already has a program posting a different one from their profile** — expected: ignored, program unchanged. Test: `ProfilePost_StudentWithProgram_CannotChangeIt` (Task 1).

---

## File Structure

| File | Responsibility |
|---|---|
| `src/EduConnect.Web/Models/AnnouncementTarget.cs` (create) | Target entity |
| `src/EduConnect.Web/Models/Announcement.cs`, `Data/ApplicationDbContext.cs` (modify) | Navigation, config, check constraint |
| `src/EduConnect.Web/Migrations/<ts>_AddAnnouncementTargets.cs` (create) | Table + backfill |
| `src/EduConnect.Web/Services/IAudienceService.cs`, `AudienceService.cs` (create) | All audience rules |
| `src/EduConnect.Web/ViewModel/AnnouncementViewModel.cs` (modify) | Target fields on the form model |
| `src/EduConnect.Web/Views/Announcement/_TargetPicker.cshtml` (create) | Hierarchy target checkboxes |
| `src/EduConnect.Web/Controllers/AnnouncementController.cs` (modify) | Create/Edit/Publish/Index/Details on the service |
| `src/EduConnect.Web/Views/Announcement/{Create,Edit}.cshtml` (modify) | Picker + submit guard |
| `src/EduConnect.Web/Services/FeedRankingService.cs`, `ChatbotService.cs`, `Controllers/HomeController.cs` (modify) | Feed and chatbot scope |
| `src/EduConnect.Web/Controllers/{DeanController,FacultyController}.cs` (modify) | Dashboard scope and names |
| `src/EduConnect.Web/Controllers/AccountController.cs` (modify) | Program lock |
| `src/EduConnect.Tests/{TestDb,AudienceServiceTests,AccountControllerTests}.cs` | Tests |
| `docs/.../spec`, `src/CLAUDE.md` (modify) | Lock rule, targeting docs |

---

### Task 1: Lock a student's program once chosen

**Files:**
- Modify: `src/EduConnect.Web/Controllers/AccountController.cs` (`FillPlacementAsync`, Profile POST placement block), `src/EduConnect.Web/Views/Account/Profile.cshtml` (placement note)
- Modify: `docs/superpowers/specs/2026-09-28-college-hierarchy-and-roles-design.md`
- Test: `src/EduConnect.Tests/AccountControllerTests.cs`

**Interfaces:**
- Produces: `ProfileViewModel.CanEditProgram` is true only for a Student with `ProgramID == null`.

- [ ] **Step 1: Write the failing tests**

A successful profile save sets `TempData`, which needs a provider. In `ControllerFakes.cs` add (with `using Microsoft.AspNetCore.Mvc.ViewFeatures;`):
```csharp
    // TempData that keeps nothing between requests.
    public sealed class NullTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }
```
and in `AccountControllerTests.Controller()`, before `return controller;`, add (with `using Microsoft.AspNetCore.Mvc.ViewFeatures;`):
```csharp
            controller.TempData = new TempDataDictionary(
                controller.ControllerContext.HttpContext, new NullTempDataProvider());
```

Append inside `AccountControllerTests`:
```csharp
        [Fact]
        public async Task ProfilePost_StudentWithProgram_CannotChangeIt()
        {
            var college = _db.AddCollege("CCIT");
            var dept = _db.AddDepartment(college, "IT&IS");
            var bsit = _db.AddProgram(dept, "BSIT");
            var bsis = _db.AddProgram(dept, "BSIS");
            var student = _db.AddUser(RoleNames.Student, college.CollegeID, dept.DepartmentID, bsit.ProgramID);
            _session.SetString("UserID", student.UserID.ToString());

            await Controller().Profile(new ProfileViewModel { ProgramID = bsis.ProgramID });

            Assert.Equal(bsit.ProgramID, (await _db.NewContext().Users.SingleAsync()).ProgramID);
        }

        [Fact]
        public async Task ProfilePost_StudentWithoutProgram_CanChooseOne()
        {
            var college = _db.AddCollege("CCIT");
            var dept = _db.AddDepartment(college, "IT&IS");
            var bsit = _db.AddProgram(dept, "BSIT");
            var student = _db.AddUser(RoleNames.Student);
            _session.SetString("UserID", student.UserID.ToString());

            await Controller().Profile(new ProfileViewModel { ProgramID = bsit.ProgramID });

            Assert.Equal(bsit.ProgramID, (await _db.NewContext().Users.SingleAsync()).ProgramID);
        }

        [Fact]
        public async Task ProfileGet_StudentWithProgram_IsReadOnly()
        {
            var college = _db.AddCollege("CCIT");
            var dept = _db.AddDepartment(college, "IT&IS");
            var bsit = _db.AddProgram(dept, "BSIT");
            var student = _db.AddUser(RoleNames.Student, college.CollegeID, dept.DepartmentID, bsit.ProgramID);
            _session.SetString("UserID", student.UserID.ToString());

            var result = Assert.IsType<ViewResult>(await Controller().Profile());

            var model = Assert.IsType<ProfileViewModel>(result.Model);
            Assert.False(model.CanEditProgram);
            Assert.Equal("BSIT · IT&IS · CCIT", model.PlacementText);
        }
```

- [ ] **Step 2: Run to verify they fail**

Run: `cd /c/EduConnect/src && dotnet test EduConnect.Tests 2>&1 | grep -E "  Failed |Passed!|Failed!"`
Expected: `ProfilePost_StudentWithProgram_CannotChangeIt` and `ProfileGet_StudentWithProgram_IsReadOnly` FAIL; `ProfilePost_StudentWithoutProgram_CanChooseOne` passes (existing behaviour, kept).

- [ ] **Step 3: Implement**

In `FillPlacementAsync`, change
```csharp
            model.CanEditProgram = user.Role.RoleName == RoleNames.Student;
```
to
```csharp
            // A student picks a program once (registration or first login);
            // after that only the admin moves them.
            model.CanEditProgram =
                user.Role.RoleName == RoleNames.Student && user.ProgramID == null;
```
In Profile POST, change `if (user.Role.RoleName == RoleNames.Student)` (the placement block) to
```csharp
            if (user.Role.RoleName == RoleNames.Student && user.ProgramID == null)
```
In `Profile.cshtml`, change the read-only note `<div class="form-text">Your administrator manages this.</div>` to
```cshtml
                            <div class="form-text">Contact your administrator to change this.</div>
```

- [ ] **Step 4: Run to verify they pass**

Run: `cd /c/EduConnect/src && dotnet test EduConnect.Tests 2>&1 | tail -1`
Expected: `Passed!  - Failed: 0, Passed: 74`.

- [ ] **Step 5: Update the spec**

In the spec's Decisions table, change row 5's decision to: `Every existing student must be placed in their real program. A student chooses their program once (registration, or first login if missing); after that it is locked and only the admin changes it (decided 2026-09-28).` In **Registration and profile**, replace the bullet `Student profile: can change program (applies immediately, no re-verification — assumption, see Open items).` with `Student profile: shows the program read-only once set; a student without one chooses it there.`, and delete the first Open items bullet (the re-verification assumption).

- [ ] **Step 6: Commit**

```bash
cd /c/EduConnect && git add src docs && git commit -m "Lock a student's program once they have chosen it

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: Announcement targets table and backfill

**Files:**
- Create: `src/EduConnect.Web/Models/AnnouncementTarget.cs`, `src/EduConnect.Web/Migrations/<ts>_AddAnnouncementTargets.cs` (+ Designer, snapshot)
- Modify: `src/EduConnect.Web/Models/Announcement.cs`, `src/EduConnect.Web/Data/ApplicationDbContext.cs`
- Modify: `src/EduConnect.Tests/TestDb.cs`; Test: `src/EduConnect.Tests/AudienceServiceTests.cs` (created here)

**Interfaces:**
- Produces: entity `AnnouncementTarget { AnnouncementTargetID, AnnouncementID, int? CollegeID, int? DepartmentID, int? ProgramID, CreatedAt; Announcement, College?, Department?, AcademicProgram? }`, `Announcement.AnnouncementTargets`, `DbSet<AnnouncementTarget> AnnouncementTargets`; TestDb helpers `AddAnnouncement(User author, string title) : Announcement`, `Target(Announcement a, College? c = null, Department? d = null, AcademicProgram? p = null)`, `TagAnnouncement(Announcement a, DepartmentTag t)`, `TagUser(User u, DepartmentTag t, bool primary = true)`.

- [ ] **Step 1: TestDb helpers**

In `TestDb`, add after `AddTag`:
```csharp
        public Announcement AddAnnouncement(User author, string title)
        {
            var category = Context.AnnouncementCategories.FirstOrDefault();
            if (category == null)
            {
                category = new AnnouncementCategory { CategoryName = "General", ColorHex = "#000000", FeedType = "Academic" };
                Context.AnnouncementCategories.Add(category);
                Context.SaveChanges();
            }

            var announcement = new Announcement
            {
                AuthorID = author.UserID,
                CategoryID = category.CategoryID,
                Title = title,
                Body = title,
                Status = "Published",
                ApprovalStatus = "Approved",
                PublishedAt = DateTime.Now
            };
            Context.Announcements.Add(announcement);
            Context.SaveChanges();
            return announcement;
        }

        public void Target(Announcement a, College? c = null, Department? d = null, AcademicProgram? p = null)
        {
            Context.AnnouncementTargets.Add(new AnnouncementTarget
            {
                AnnouncementID = a.AnnouncementID,
                CollegeID = c?.CollegeID,
                DepartmentID = d?.DepartmentID,
                ProgramID = p?.ProgramID
            });
            Context.SaveChanges();
        }

        public void TagAnnouncement(Announcement a, DepartmentTag t)
        {
            Context.AnnouncementTags.Add(new AnnouncementTag { AnnouncementID = a.AnnouncementID, TagID = t.TagID });
            Context.SaveChanges();
        }

        public void TagUser(User u, DepartmentTag t, bool primary = true)
        {
            Context.UserDepartments.Add(new UserDepartment { UserID = u.UserID, TagID = t.TagID, IsPrimary = primary });
            Context.SaveChanges();
        }
```

- [ ] **Step 2: Write the failing test**

`src/EduConnect.Tests/AudienceServiceTests.cs`:
```csharp
using EduConnect.Web;
using EduConnect.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace EduConnect.Tests
{
    public class AudienceServiceTests : IDisposable
    {
        private readonly TestDb _db = new();

        public void Dispose() => _db.Dispose();

        [Fact]
        public void Target_WithTwoLevels_IsRejectedByTheDatabase()
        {
            var college = _db.AddCollege("CCIT");
            var dept = _db.AddDepartment(college, "IT&IS");
            var author = _db.AddUser(RoleNames.Dean, college.CollegeID);
            var a = _db.AddAnnouncement(author, "Two levels");

            Assert.Throws<DbUpdateException>(() => _db.Target(a, college, dept));
        }
    }
}
```

- [ ] **Step 3: Run to verify it fails**

Run: `cd /c/EduConnect/src && dotnet test EduConnect.Tests 2>&1 | grep -E "error CS" | sort -u | head -2`
Expected: build FAILS — `The type or namespace name 'AnnouncementTarget' could not be found`.

- [ ] **Step 4: Entity and configuration**

`src/EduConnect.Web/Models/AnnouncementTarget.cs`:
```csharp
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EduConnect.Web.Models
{
    // Who an announcement is for in the academic hierarchy. Exactly one
    // level per row (check constraint), stored at the level the author
    // chose so later-added programs of a targeted college are included.
    [Table("AnnouncementTargets")]
    public class AnnouncementTarget
    {
        [Key]
        public int AnnouncementTargetID { get; set; }

        [Required]
        public int AnnouncementID { get; set; }

        public int? CollegeID { get; set; }
        public int? DepartmentID { get; set; }
        public int? ProgramID { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public Announcement Announcement { get; set; } = null!;
        public College? College { get; set; }
        public Department? Department { get; set; }
        public AcademicProgram? AcademicProgram { get; set; }
    }
}
```
In `Announcement.cs`, after `public ICollection<AnnouncementTag> AnnouncementTags { get; set; }` add:
```csharp
        public ICollection<AnnouncementTarget> AnnouncementTargets { get; set; } = new List<AnnouncementTarget>();
```
In `ApplicationDbContext`, add `public DbSet<AnnouncementTarget> AnnouncementTargets { get; set; }` after the `AnnouncementTags` DbSet, and after the `// ─── AnnouncementTags (Junction) ───` block add:
```csharp
            // ─── AnnouncementTargets ───────────────
            modelBuilder.Entity<AnnouncementTarget>(entity =>
            {
                entity.HasKey(e => e.AnnouncementTargetID);
                entity.ToTable(t => t.HasCheckConstraint(
                    "CK_AnnouncementTargets_OneLevel",
                    "(CASE WHEN CollegeID IS NULL THEN 0 ELSE 1 END) + " +
                    "(CASE WHEN DepartmentID IS NULL THEN 0 ELSE 1 END) + " +
                    "(CASE WHEN ProgramID IS NULL THEN 0 ELSE 1 END) = 1"));
                entity.HasOne(e => e.Announcement)
                      .WithMany(e => e.AnnouncementTargets)
                      .HasForeignKey(e => e.AnnouncementID);
                entity.HasOne(e => e.College)
                      .WithMany()
                      .HasForeignKey(e => e.CollegeID)
                      .OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(e => e.Department)
                      .WithMany()
                      .HasForeignKey(e => e.DepartmentID)
                      .OnDelete(DeleteBehavior.Restrict);
                entity.HasOne(e => e.AcademicProgram)
                      .WithMany()
                      .HasForeignKey(e => e.ProgramID)
                      .OnDelete(DeleteBehavior.Restrict);
            });
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `cd /c/EduConnect/src && dotnet test EduConnect.Tests 2>&1 | tail -1` → `Passed!  - Failed: 0, Passed: 75`.

- [ ] **Step 6: Migration with backfill**

Stop the server; `cd /c/EduConnect/src && dotnet ef migrations add AddAnnouncementTargets --project EduConnect.Web`. Confirm `Up` only creates `AnnouncementTargets` (with the check constraint, 4 indexes, 4 FKs) — stop and report anything else. Append to the end of `Up`:
```csharp
            // Existing announcements were tagged with what are now colleges:
            // give each the matching college target so its audience is
            // unchanged. The legacy tag rows stay until Plan 5.
            migrationBuilder.Sql(@"
                INSERT INTO AnnouncementTargets (AnnouncementID, CollegeID, CreatedAt)
                SELECT DISTINCT at.AnnouncementID, c.CollegeID, SYSDATETIME()
                FROM AnnouncementTags at
                JOIN Colleges c ON c.LegacyTagID = at.TagID
                WHERE NOT EXISTS (
                    SELECT 1 FROM AnnouncementTargets x
                    WHERE x.AnnouncementID = at.AnnouncementID AND x.CollegeID = c.CollegeID);
            ");
```

- [ ] **Step 7: Apply and assert (Review Focus 3)**

```bash
cd /c/EduConnect/src && dotnet build EduConnect.Web 2>&1 | grep -E " error |Build succeeded" | sort -u && dotnet ef database update --project EduConnect.Web
sqlcmd -S 'localhost\SQLEXPRESS' -d EduConnectDB -E -C -W -Q "SET NOCOUNT ON; SELECT (SELECT COUNT(*) FROM AnnouncementTags at JOIN Colleges c ON c.LegacyTagID=at.TagID) collegeTagged, (SELECT COUNT(*) FROM AnnouncementTargets) targets, (SELECT COUNT(*) FROM AnnouncementTags at JOIN Colleges c ON c.LegacyTagID=at.TagID WHERE NOT EXISTS (SELECT 1 FROM AnnouncementTargets x WHERE x.AnnouncementID=at.AnnouncementID AND x.CollegeID=c.CollegeID)) missing"
```
Expected: `targets` = `collegeTagged` (8 locally on 2026-09-28: CCIT 6, COA 1, COS 1), `missing` 0.

- [ ] **Step 8: Commit**

```bash
cd /c/EduConnect && git add src && git commit -m "Add announcement targets and carry over college tags

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 3: Who sees an announcement

**Files:**
- Create: `src/EduConnect.Web/Services/IAudienceService.cs`, `src/EduConnect.Web/Services/AudienceService.cs`
- Modify: `src/EduConnect.Web/Program.cs`
- Test: `src/EduConnect.Tests/AudienceServiceTests.cs`

**Interfaces:**
- Produces:
  - `record Viewer(int UserID, string RoleName, int? CollegeID, int? DepartmentID, int? ProgramID, IReadOnlyList<int> TagIDs)` (namespace `EduConnect.Web.Services`)
  - `IAudienceService.GetViewerAsync(int userId) : Task<Viewer>` (unknown user → role `""`, no placement, no tags)
  - `IAudienceService.VisibleTo(Viewer v) : Expression<Func<Announcement, bool>>`
  - `IAudienceService.AddressedTo(Viewer v) : Expression<Func<Announcement, bool>>`
  - `static AudienceService.Not(Expression<Func<Announcement,bool>> e)`
  - Test fixture fields reused by Tasks 4–5: `_ccit, _itis, _cs, _bsit, _bsis, _bscs, _cos, _bio, _bsbio, _all, _office` and users `_studentBsit, _faculty, _chair, _dean, _staff, _studentBio`.

- [ ] **Step 1: Write the failing tests**

Replace the body of `AudienceServiceTests` (keep the check-constraint test) so the class reads:
```csharp
    public class AudienceServiceTests : IDisposable
    {
        private readonly TestDb _db = new();
        private readonly College _ccit, _cos;
        private readonly Department _itis, _cs, _bio;
        private readonly AcademicProgram _bsit, _bsis, _bscs, _bsbio;
        private readonly DepartmentTag _all, _office;
        private readonly User _studentBsit, _faculty, _chair, _dean, _staff, _studentBio;

        public AudienceServiceTests()
        {
            _ccit = _db.AddCollege("CCIT");
            _itis = _db.AddDepartment(_ccit, "IT&IS");
            _cs = _db.AddDepartment(_ccit, "CS");
            _bsit = _db.AddProgram(_itis, "BSIT");
            _bsis = _db.AddProgram(_itis, "BSIS");
            _bscs = _db.AddProgram(_cs, "BSCS");
            _cos = _db.AddCollege("COS");
            _bio = _db.AddDepartment(_cos, "Biology");
            _bsbio = _db.AddProgram(_bio, "BSBio");
            _all = _db.AddTag("ALL");
            _office = _db.AddTag("REG");

            _studentBsit = _db.AddUser(RoleNames.Student, _ccit.CollegeID, _itis.DepartmentID, _bsit.ProgramID);
            _faculty = _db.AddUser(RoleNames.Faculty, _ccit.CollegeID, _itis.DepartmentID);
            _chair = _db.AddUser(RoleNames.Chairperson, _ccit.CollegeID, _itis.DepartmentID);
            _dean = _db.AddUser(RoleNames.Dean, _ccit.CollegeID);
            _staff = _db.AddUser(RoleNames.Staff);
            _db.TagUser(_staff, _office);
            _studentBio = _db.AddUser(RoleNames.Student, _cos.CollegeID, _bio.DepartmentID, _bsbio.ProgramID);

            var author = _db.AddUser(RoleNames.Administrator);
            _db.Target(_db.AddAnnouncement(author, "P-BSIT"), p: _bsit);
            _db.Target(_db.AddAnnouncement(author, "D-ITIS"), d: _itis);
            _db.Target(_db.AddAnnouncement(author, "C-CCIT"), c: _ccit);
            _db.Target(_db.AddAnnouncement(author, "P-BSCS"), p: _bscs);
            _db.Target(_db.AddAnnouncement(author, "D-CS"), d: _cs);
            _db.Target(_db.AddAnnouncement(author, "C-COS"), c: _cos);
            _db.TagAnnouncement(_db.AddAnnouncement(author, "ALL"), _all);
            _db.TagAnnouncement(_db.AddAnnouncement(author, "OFFICE"), _office);
        }

        public void Dispose() => _db.Dispose();

        private AudienceService Service => new(_db.Context);

        private async Task<string[]> Visible(User u)
        {
            var viewer = await Service.GetViewerAsync(u.UserID);
            return await _db.NewContext().Announcements
                .Where(Service.VisibleTo(viewer))
                .Select(a => a.Title).OrderBy(t => t).ToArrayAsync();
        }

        private async Task<string[]> Addressed(User u)
        {
            var viewer = await Service.GetViewerAsync(u.UserID);
            return await _db.NewContext().Announcements
                .Where(Service.AddressedTo(viewer))
                .Select(a => a.Title).OrderBy(t => t).ToArrayAsync();
        }

        [Fact]
        public void Target_WithTwoLevels_IsRejectedByTheDatabase()
        {
            var a = _db.AddAnnouncement(_dean, "Two levels");

            Assert.Throws<DbUpdateException>(() => _db.Target(a, _ccit, _itis));
        }

        [Fact]
        public async Task GetViewer_LoadsPlacementAndTags()
        {
            var v = await Service.GetViewerAsync(_staff.UserID);

            Assert.Equal(RoleNames.Staff, v.RoleName);
            Assert.Equal(new[] { _office.TagID }, v.TagIDs);
        }

        [Fact]
        public async Task VisibleTo_Student_SeesProgramDepartmentCollegeAndSchoolWide() =>
            Assert.Equal(new[] { "ALL", "C-CCIT", "D-ITIS", "P-BSIT" }, await Visible(_studentBsit));

        [Fact]
        public async Task VisibleTo_Student_SeesCollegeTarget() =>
            Assert.Contains("C-COS", await Visible(_studentBio));

        [Fact]
        public async Task VisibleTo_Faculty_SeesOwnDepartmentsProgramsButNotOthers() =>
            Assert.Equal(new[] { "ALL", "C-CCIT", "D-ITIS", "P-BSIT" }, await Visible(_faculty));

        [Fact]
        public async Task VisibleTo_Chairperson_OnlyOwnDepartmentAndCollege() =>
            Assert.Equal(new[] { "ALL", "C-CCIT", "D-ITIS", "P-BSIT" }, await Visible(_chair));

        [Fact]
        public async Task VisibleTo_Dean_EverythingInsideTheCollegeOnly() =>
            Assert.Equal(new[] { "ALL", "C-CCIT", "D-CS", "D-ITIS", "P-BSCS", "P-BSIT" }, await Visible(_dean));

        [Fact]
        public async Task VisibleTo_Staff_TagsAndSchoolWideOnly() =>
            Assert.Equal(new[] { "ALL", "OFFICE" }, await Visible(_staff));

        [Fact]
        public async Task VisibleTo_Author_AlwaysSeesOwnAnnouncement()
        {
            _db.Target(_db.AddAnnouncement(_faculty, "Mine"), p: _bsbio);

            Assert.Contains("Mine", await Visible(_faculty));
        }

        [Fact]
        public async Task AddressedTo_ExcludesSchoolWide() =>
            Assert.Equal(new[] { "C-CCIT", "D-ITIS", "P-BSIT" }, await Addressed(_studentBsit));

        [Fact]
        public async Task Not_InvertsVisibleTo()
        {
            var viewer = await Service.GetViewerAsync(_studentBsit.UserID);

            var hidden = await _db.NewContext().Announcements
                .Where(AudienceService.Not(Service.VisibleTo(viewer)))
                .Select(a => a.Title).OrderBy(t => t).ToArrayAsync();

            Assert.Equal(new[] { "C-COS", "D-CS", "OFFICE", "P-BSCS" }, hidden);
        }
    }
```
Add `using EduConnect.Web.Services;` at the top.

- [ ] **Step 2: Run to verify they fail**

Run: `cd /c/EduConnect/src && dotnet test EduConnect.Tests 2>&1 | grep -E "error CS" | sort -u | head -2`
Expected: `The type or namespace name 'AudienceService' could not be found`.

- [ ] **Step 3: Interface**

`src/EduConnect.Web/Services/IAudienceService.cs`:
```csharp
using System.Linq.Expressions;
using EduConnect.Web.Models;

namespace EduConnect.Web.Services
{
    // Everything that decides an announcement's audience: who reads it,
    // who is notified, and what an author may target.
    public record Viewer(
        int UserID,
        string RoleName,
        int? CollegeID,
        int? DepartmentID,
        int? ProgramID,
        IReadOnlyList<int> TagIDs);

    public interface IAudienceService
    {
        Task<Viewer> GetViewerAsync(int userId);

        // Feed scope: School Wide, the viewer's tags, their own posts, and
        // targets that reach them (see the plan's Global Constraints).
        Expression<Func<Announcement, bool>> VisibleTo(Viewer viewer);

        // VisibleTo without School Wide or authorship: what is specifically
        // for the viewer's program/department/college or tags.
        Expression<Func<Announcement, bool>> AddressedTo(Viewer viewer);
    }
}
```

- [ ] **Step 4: Implementation**

`src/EduConnect.Web/Services/AudienceService.cs`:
```csharp
using System.Linq.Expressions;
using EduConnect.Web.Data;
using EduConnect.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace EduConnect.Web.Services
{
    public class AudienceService : IAudienceService
    {
        private const string SchoolWide = "ALL";

        private readonly ApplicationDbContext _context;

        public AudienceService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<Viewer> GetViewerAsync(int userId)
        {
            var user = await _context.Users
                .Where(u => u.UserID == userId)
                .Select(u => new
                {
                    u.UserID,
                    Role = u.Role.RoleName,
                    u.CollegeID,
                    u.DepartmentID,
                    u.ProgramID
                })
                .FirstOrDefaultAsync();

            if (user == null)
                return new Viewer(userId, "", null, null, null, Array.Empty<int>());

            var tagIds = await _context.UserDepartments
                .Where(ud => ud.UserID == userId)
                .Select(ud => ud.TagID)
                .ToListAsync();

            return new Viewer(user.UserID, user.Role, user.CollegeID,
                user.DepartmentID, user.ProgramID, tagIds);
        }

        public Expression<Func<Announcement, bool>> VisibleTo(Viewer viewer) =>
            Build(viewer, includeSchoolWideAndOwn: true);

        public Expression<Func<Announcement, bool>> AddressedTo(Viewer viewer) =>
            Build(viewer, includeSchoolWideAndOwn: false);

        public static Expression<Func<Announcement, bool>> Not(
            Expression<Func<Announcement, bool>> expression) =>
            Expression.Lambda<Func<Announcement, bool>>(
                Expression.Not(expression.Body), expression.Parameters);

        // One lambda per role so EF translates each to a single WHERE.
        // Captured locals become SQL parameters.
        private static Expression<Func<Announcement, bool>> Build(Viewer v, bool includeSchoolWideAndOwn)
        {
            var tagIds = v.TagIDs.ToList();
            var userId = v.UserID;
            var collegeId = v.CollegeID;
            var departmentId = v.DepartmentID;
            var programId = v.ProgramID;
            var wide = includeSchoolWideAndOwn;

            switch (v.RoleName)
            {
                case RoleNames.Student:
                case RoleNames.StudentPending:
                    return a =>
                        (wide && (a.AuthorID == userId ||
                                  a.AnnouncementTags.Any(t => t.DepartmentTag.ShortName == SchoolWide))) ||
                        a.AnnouncementTags.Any(t => tagIds.Contains(t.TagID)) ||
                        a.AnnouncementTargets.Any(t =>
                            (programId != null && t.ProgramID == programId) ||
                            (departmentId != null && t.DepartmentID == departmentId) ||
                            (collegeId != null && t.CollegeID == collegeId));

                case RoleNames.Chairperson:
                case RoleNames.Faculty:
                    return a =>
                        (wide && (a.AuthorID == userId ||
                                  a.AnnouncementTags.Any(t => t.DepartmentTag.ShortName == SchoolWide))) ||
                        a.AnnouncementTags.Any(t => tagIds.Contains(t.TagID)) ||
                        a.AnnouncementTargets.Any(t =>
                            (departmentId != null &&
                                (t.DepartmentID == departmentId ||
                                 t.AcademicProgram!.DepartmentID == departmentId)) ||
                            (collegeId != null && t.CollegeID == collegeId));

                case RoleNames.Dean:
                    return a =>
                        (wide && (a.AuthorID == userId ||
                                  a.AnnouncementTags.Any(t => t.DepartmentTag.ShortName == SchoolWide))) ||
                        a.AnnouncementTags.Any(t => tagIds.Contains(t.TagID)) ||
                        a.AnnouncementTargets.Any(t =>
                            collegeId != null &&
                                (t.CollegeID == collegeId ||
                                 t.Department!.CollegeID == collegeId ||
                                 t.AcademicProgram!.Department.CollegeID == collegeId));

                default:
                    // Staff, and any role without a placement: tags only.
                    return a =>
                        (wide && (a.AuthorID == userId ||
                                  a.AnnouncementTags.Any(t => t.DepartmentTag.ShortName == SchoolWide))) ||
                        a.AnnouncementTags.Any(t => tagIds.Contains(t.TagID));
            }
        }
    }
}
```

- [ ] **Step 5: Run to verify they pass**

Run: `cd /c/EduConnect/src && dotnet test EduConnect.Tests 2>&1 | tail -1`
Expected: `Passed!  - Failed: 0, Passed: 85`.

- [ ] **Step 6: Register and commit**

In `Program.cs`, after the `IPlacementService` registration add:
```csharp
builder.Services.AddScoped<EduConnect.Web.Services.IAudienceService, EduConnect.Web.Services.AudienceService>();
```
```bash
cd /c/EduConnect/src && dotnet build EduConnect.Web 2>&1 | grep -E " error |Build succeeded" | sort -u
cd /c/EduConnect && git add src && git commit -m "Decide who sees an announcement in one place

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 4: What an author may target

**Files:**
- Modify: `src/EduConnect.Web/Services/IAudienceService.cs`, `src/EduConnect.Web/Services/AudienceService.cs`
- Test: `src/EduConnect.Tests/AudienceServiceTests.cs`

**Interfaces:**
- Consumes: `HierarchyResult` (Plan 2).
- Produces:
  - `class TargetOptions { College? College; bool CanTargetCollege; bool CanTargetDepartments; }` — `College` carries only the departments/programs the author may pick (active only; implicit departments included so their programs show).
  - `record TargetSelection(IReadOnlyList<int> CollegeIDs, IReadOnlyList<int> DepartmentIDs, IReadOnlyList<int> ProgramIDs) { bool IsEmpty }`
  - `IAudienceService.GetTargetOptionsAsync(int authorId) : Task<TargetOptions>`
  - `IAudienceService.ValidateTargetsAsync(int authorId, TargetSelection selection) : Task<HierarchyResult>`

- [ ] **Step 1: Write the failing tests**

Append inside `AudienceServiceTests`:
```csharp
        private static TargetSelection Sel(int[]? c = null, int[]? d = null, int[]? p = null) =>
            new(c ?? Array.Empty<int>(), d ?? Array.Empty<int>(), p ?? Array.Empty<int>());

        [Fact]
        public async Task Options_Dean_WholeCollege()
        {
            var o = await Service.GetTargetOptionsAsync(_dean.UserID);

            Assert.True(o.CanTargetCollege);
            Assert.True(o.CanTargetDepartments);
            Assert.Equal(_ccit.CollegeID, o.College!.CollegeID);
            Assert.Equal(new[] { "CS", "IT&IS" }, o.College.Departments.Select(d => d.Name).OrderBy(n => n));
            Assert.Equal(3, o.College.Departments.SelectMany(d => d.Programs).Count());
        }

        [Fact]
        public async Task Options_Chairperson_OwnDepartmentOnly()
        {
            var o = await Service.GetTargetOptionsAsync(_chair.UserID);

            Assert.False(o.CanTargetCollege);
            Assert.True(o.CanTargetDepartments);
            var dept = Assert.Single(o.College!.Departments);
            Assert.Equal("IT&IS", dept.Name);
            Assert.Equal(new[] { "BSIS", "BSIT" }, dept.Programs.Select(p => p.Name).OrderBy(n => n));
        }

        [Fact]
        public async Task Options_Faculty_ProgramsOnly()
        {
            var o = await Service.GetTargetOptionsAsync(_faculty.UserID);

            Assert.False(o.CanTargetCollege);
            Assert.False(o.CanTargetDepartments);
            Assert.Equal(new[] { "BSIS", "BSIT" }, o.College!.Departments.Single().Programs.Select(p => p.Name).OrderBy(n => n));
        }

        [Fact]
        public async Task Options_RetiredProgram_NotOffered()
        {
            _bsis.IsActive = false;
            _db.Context.SaveChanges();

            var o = await Service.GetTargetOptionsAsync(_faculty.UserID);

            Assert.Equal("BSIT", o.College!.Departments.Single().Programs.Single().Name);
        }

        [Fact]
        public async Task Options_UnplacedFaculty_None()
        {
            var unplaced = _db.AddUser(RoleNames.Faculty);

            Assert.Null((await Service.GetTargetOptionsAsync(unplaced.UserID)).College);
        }

        [Fact]
        public async Task Validate_FacultyOwnProgram_Ok() =>
            Assert.True((await Service.ValidateTargetsAsync(_faculty.UserID, Sel(p: new[] { _bsit.ProgramID }))).Ok);

        [Fact]
        public async Task Validate_FacultyOtherDepartmentsProgram_Fails() =>
            Assert.False((await Service.ValidateTargetsAsync(_faculty.UserID, Sel(p: new[] { _bscs.ProgramID }))).Ok);

        [Fact]
        public async Task Validate_FacultyDepartment_Fails() =>
            Assert.False((await Service.ValidateTargetsAsync(_faculty.UserID, Sel(d: new[] { _itis.DepartmentID }))).Ok);

        [Fact]
        public async Task Validate_FacultyCollege_Fails() =>
            Assert.False((await Service.ValidateTargetsAsync(_faculty.UserID, Sel(c: new[] { _ccit.CollegeID }))).Ok);

        [Fact]
        public async Task Validate_ChairpersonOwnDepartment_Ok() =>
            Assert.True((await Service.ValidateTargetsAsync(_chair.UserID, Sel(d: new[] { _itis.DepartmentID }))).Ok);

        [Fact]
        public async Task Validate_ChairpersonCollege_Fails() =>
            Assert.False((await Service.ValidateTargetsAsync(_chair.UserID, Sel(c: new[] { _ccit.CollegeID }))).Ok);

        [Fact]
        public async Task Validate_DeanCollegeAndOtherDepartmentsProgram_Ok() =>
            Assert.True((await Service.ValidateTargetsAsync(_dean.UserID, Sel(c: new[] { _ccit.CollegeID }, p: new[] { _bscs.ProgramID }))).Ok);

        [Fact]
        public async Task Validate_DeanOtherCollegesProgram_Fails() =>
            Assert.False((await Service.ValidateTargetsAsync(_dean.UserID, Sel(p: new[] { _bsbio.ProgramID }))).Ok);

        [Fact]
        public async Task Validate_EmptySelection_Ok() =>
            Assert.True((await Service.ValidateTargetsAsync(_faculty.UserID, Sel())).Ok);
```

- [ ] **Step 2: Run to verify they fail**

Run: `cd /c/EduConnect/src && dotnet test EduConnect.Tests 2>&1 | grep -E "error CS" | sort -u | head -2`
Expected: `'AudienceService' does not contain a definition for 'GetTargetOptionsAsync'` (or `TargetSelection` not found).

- [ ] **Step 3: Interface additions**

In `IAudienceService.cs`, before the interface add:
```csharp
    // The part of the hierarchy an author may pick from. College holds only
    // the departments and programs they may target.
    public class TargetOptions
    {
        public College? College { get; set; }
        public bool CanTargetCollege { get; set; }
        public bool CanTargetDepartments { get; set; }
    }

    public record TargetSelection(
        IReadOnlyList<int> CollegeIDs,
        IReadOnlyList<int> DepartmentIDs,
        IReadOnlyList<int> ProgramIDs)
    {
        public bool IsEmpty =>
            CollegeIDs.Count == 0 && DepartmentIDs.Count == 0 && ProgramIDs.Count == 0;
    }
```
and inside the interface add:
```csharp

        // Dean: own college; Chairperson: own department; Faculty: own
        // department's programs only. Unplaced authors: none.
        Task<TargetOptions> GetTargetOptionsAsync(int authorId);

        // Refuses any posted target outside GetTargetOptionsAsync.
        Task<HierarchyResult> ValidateTargetsAsync(int authorId, TargetSelection selection);
```

- [ ] **Step 4: Implementation**

In `AudienceService`, add:
```csharp
        public async Task<TargetOptions> GetTargetOptionsAsync(int authorId)
        {
            var author = await _context.Users
                .Where(u => u.UserID == authorId)
                .Select(u => new { Role = u.Role.RoleName, u.CollegeID, u.DepartmentID })
                .FirstOrDefaultAsync();

            var options = new TargetOptions();
            if (author?.CollegeID == null)
                return options;

            var isDean = author.Role == RoleNames.Dean;
            var isStaff = author.Role is RoleNames.Chairperson or RoleNames.Faculty;
            if (!isDean && !(isStaff && author.DepartmentID != null))
                return options;

            var college = await _context.Colleges
                .Include(c => c.Departments)
                    .ThenInclude(d => d.Programs)
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.CollegeID == author.CollegeID && c.IsActive);
            if (college == null)
                return options;

            college.Departments = college.Departments
                .Where(d => d.IsActive && (isDean || d.DepartmentID == author.DepartmentID))
                .OrderBy(d => d.Name)
                .ToList();
            foreach (var dept in college.Departments)
                dept.Programs = dept.Programs
                    .Where(p => p.IsActive)
                    .OrderBy(p => p.Name)
                    .ToList();

            options.College = college;
            options.CanTargetCollege = isDean;
            options.CanTargetDepartments = isDean || author.Role == RoleNames.Chairperson;
            return options;
        }

        public async Task<HierarchyResult> ValidateTargetsAsync(int authorId, TargetSelection selection)
        {
            if (selection.IsEmpty)
                return HierarchyResult.Success;

            var options = await GetTargetOptionsAsync(authorId);
            var college = options.College;

            var colleges = options.CanTargetCollege && college != null
                ? new HashSet<int> { college.CollegeID }
                : new HashSet<int>();
            var departments = options.CanTargetDepartments && college != null
                ? college.Departments.Where(d => !d.IsImplicit).Select(d => d.DepartmentID).ToHashSet()
                : new HashSet<int>();
            var programs = college?.Departments.SelectMany(d => d.Programs).Select(p => p.ProgramID).ToHashSet()
                ?? new HashSet<int>();

            if (selection.CollegeIDs.Any(id => !colleges.Contains(id)) ||
                selection.DepartmentIDs.Any(id => !departments.Contains(id)) ||
                selection.ProgramIDs.Any(id => !programs.Contains(id)))
                return HierarchyResult.Fail(
                    "You can only post to your own " +
                    (options.CanTargetCollege ? "college." :
                     options.CanTargetDepartments ? "department and its programs." :
                     "department's programs."));

            return HierarchyResult.Success;
        }
```

- [ ] **Step 5: Run to verify they pass**

Run: `cd /c/EduConnect/src && dotnet test EduConnect.Tests 2>&1 | tail -1`
Expected: `Passed!  - Failed: 0, Passed: 99`.

- [ ] **Step 6: Commit**

```bash
cd /c/EduConnect && git add src && git commit -m "Decide what each author may target

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 5: Recipients and target labels

**Files:**
- Modify: `src/EduConnect.Web/Services/IAudienceService.cs`, `src/EduConnect.Web/Services/AudienceService.cs`
- Test: `src/EduConnect.Tests/AudienceServiceTests.cs`

**Interfaces:**
- Produces:
  - `IAudienceService.GetRecipientIdsAsync(int announcementId, int excludeUserId) : Task<List<int>>`
  - `IAudienceService.AddTargetLabelsAsync(IEnumerable<AnnouncementTableViewModel> rows) : Task` — appends short labels (program/department/college `ShortName ?? Name`) to each row's `Tags`, de-duplicated.
  - `IAudienceService.GetTargetNamesAsync(int announcementId) : Task<List<string>>` — full names, for Details.

- [ ] **Step 1: Write the failing tests**

Append inside `AudienceServiceTests` (add `using EduConnect.Web.ViewModels;` at the top):
```csharp
        private async Task<int[]> RecipientsOf(string title, int exclude = 0)
        {
            var id = await _db.NewContext().Announcements.Where(a => a.Title == title).Select(a => a.AnnouncementID).SingleAsync();
            return (await Service.GetRecipientIdsAsync(id, exclude)).OrderBy(x => x).ToArray();
        }

        private static int[] Ids(params User[] users) => users.Select(u => u.UserID).OrderBy(x => x).ToArray();

        [Fact]
        public async Task Recipients_ProgramTarget_OnlyItsStudents() =>
            Assert.Equal(Ids(_studentBsit), await RecipientsOf("P-BSIT"));

        [Fact]
        public async Task Recipients_DepartmentTarget_StudentsFacultyAndChair() =>
            Assert.Equal(Ids(_studentBsit, _faculty, _chair), await RecipientsOf("D-ITIS"));

        [Fact]
        public async Task Recipients_CollegeTarget_EveryonePlacedInIt() =>
            Assert.Equal(Ids(_studentBsit, _faculty, _chair, _dean), await RecipientsOf("C-CCIT"));

        [Fact]
        public async Task Recipients_OfficeTag_TaggedUsers() =>
            Assert.Equal(Ids(_staff), await RecipientsOf("OFFICE"));

        [Fact]
        public async Task Recipients_SchoolWide_AllActiveUsersButTheAuthor()
        {
            var all = await RecipientsOf("ALL", exclude: _dean.UserID);

            Assert.DoesNotContain(_dean.UserID, all);
            Assert.Contains(_studentBio.UserID, all);
            Assert.Contains(_staff.UserID, all);
        }

        [Fact]
        public async Task Recipients_SkipInactiveUsersAndAuthor()
        {
            var pending = _db.AddUser(RoleNames.StudentPending, _ccit.CollegeID, _itis.DepartmentID, _bsit.ProgramID, isActive: false);

            var ids = await RecipientsOf("P-BSIT", exclude: _studentBsit.UserID);

            Assert.DoesNotContain(pending.UserID, ids);
            Assert.DoesNotContain(_studentBsit.UserID, ids);
        }

        [Fact]
        public async Task Labels_AppendShortNamesWithoutDuplicates()
        {
            _bsit.ShortName = "BSIT";
            _ccit.ShortName = "CCIT";
            _db.Context.SaveChanges();
            var ids = await _db.NewContext().Announcements
                .Where(a => a.Title == "P-BSIT" || a.Title == "C-CCIT")
                .OrderBy(a => a.Title)
                .Select(a => a.AnnouncementID).ToListAsync();
            var rows = ids.Select(id => new AnnouncementTableViewModel { AnnouncementID = id, Tags = new List<string> { "CCIT" } }).ToList();

            await Service.AddTargetLabelsAsync(rows);

            Assert.Equal(new[] { "CCIT" }, rows[0].Tags);          // C-CCIT: already had its college tag
            Assert.Equal(new[] { "CCIT", "BSIT" }, rows[1].Tags);  // P-BSIT
        }

        [Fact]
        public async Task TargetNames_FullNames()
        {
            var id = await _db.NewContext().Announcements.Where(a => a.Title == "D-ITIS").Select(a => a.AnnouncementID).SingleAsync();

            Assert.Equal(new[] { "IT&IS" }, await Service.GetTargetNamesAsync(id));
        }
```

- [ ] **Step 2: Run to verify they fail**

Run: `cd /c/EduConnect/src && dotnet test EduConnect.Tests 2>&1 | grep -E "error CS" | sort -u | head -2`
Expected: `'AudienceService' does not contain a definition for 'GetRecipientIdsAsync'`.

- [ ] **Step 3: Interface additions**

In `IAudienceService.cs` add `using EduConnect.Web.ViewModels;` and inside the interface:
```csharp

        // Active users an announcement reaches (see Global Constraints),
        // never including excludeUserId (the author).
        Task<List<int>> GetRecipientIdsAsync(int announcementId, int excludeUserId);

        // Appends each row's target short labels to its Tags.
        Task AddTargetLabelsAsync(IEnumerable<AnnouncementTableViewModel> rows);

        // Full names of an announcement's targets, for the details page.
        Task<List<string>> GetTargetNamesAsync(int announcementId);
```

- [ ] **Step 4: Implementation**

In `AudienceService` add `using EduConnect.Web.ViewModels;` and:
```csharp
        public async Task<List<int>> GetRecipientIdsAsync(int announcementId, int excludeUserId)
        {
            var tags = await _context.AnnouncementTags
                .Where(t => t.AnnouncementID == announcementId)
                .Select(t => new { t.TagID, t.DepartmentTag.ShortName })
                .ToListAsync();

            var active = _context.Users.Where(u => u.IsActive && u.UserID != excludeUserId);

            if (tags.Any(t => t.ShortName == SchoolWide))
                return await active.Select(u => u.UserID).ToListAsync();

            var targets = await _context.AnnouncementTargets
                .Where(t => t.AnnouncementID == announcementId)
                .ToListAsync();

            var tagIds = tags.Select(t => t.TagID).ToList();
            var collegeIds = targets.Where(t => t.CollegeID != null).Select(t => t.CollegeID).ToList();
            var departmentIds = targets.Where(t => t.DepartmentID != null).Select(t => t.DepartmentID).ToList();
            var programIds = targets.Where(t => t.ProgramID != null).Select(t => t.ProgramID).ToList();

            return await active
                .Where(u =>
                    u.UserDepartments.Any(ud => tagIds.Contains(ud.TagID)) ||
                    (u.CollegeID != null && collegeIds.Contains(u.CollegeID)) ||
                    (u.DepartmentID != null && departmentIds.Contains(u.DepartmentID)) ||
                    (u.ProgramID != null && programIds.Contains(u.ProgramID)))
                .Select(u => u.UserID)
                .ToListAsync();
        }

        public async Task AddTargetLabelsAsync(IEnumerable<AnnouncementTableViewModel> rows)
        {
            var list = rows.ToList();
            var ids = list.Select(r => r.AnnouncementID).ToList();
            if (ids.Count == 0)
                return;

            var labels = await _context.AnnouncementTargets
                .Where(t => ids.Contains(t.AnnouncementID))
                .Select(t => new
                {
                    t.AnnouncementID,
                    Label = t.ProgramID != null
                        ? (t.AcademicProgram!.ShortName ?? t.AcademicProgram.Name)
                        : t.DepartmentID != null
                            ? (t.Department!.ShortName ?? t.Department.Name)
                            : (t.College!.ShortName ?? t.College.Name)
                })
                .ToListAsync();

            foreach (var row in list)
                foreach (var label in labels.Where(l => l.AnnouncementID == row.AnnouncementID))
                    if (!row.Tags.Contains(label.Label))
                        row.Tags.Add(label.Label);
        }

        public Task<List<string>> GetTargetNamesAsync(int announcementId) =>
            _context.AnnouncementTargets
                .Where(t => t.AnnouncementID == announcementId)
                .Select(t => t.ProgramID != null ? t.AcademicProgram!.Name
                    : t.DepartmentID != null ? t.Department!.Name
                    : t.College!.Name)
                .ToListAsync();
```

- [ ] **Step 5: Run to verify they pass**

Run: `cd /c/EduConnect/src && dotnet test EduConnect.Tests 2>&1 | tail -1`
Expected: `Passed!  - Failed: 0, Passed: 107`.

- [ ] **Step 6: Commit**

```bash
cd /c/EduConnect && git add src && git commit -m "Decide who is notified about an announcement

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 6: Target picker on Create and Edit

**Files:**
- Modify: `src/EduConnect.Web/ViewModel/AnnouncementViewModel.cs`, `src/EduConnect.Web/Controllers/AnnouncementController.cs` (ctor; `GetSelectableTagsAsync`; Create GET/POST; Edit GET/POST; Publish), `src/EduConnect.Web/Views/Announcement/Create.cshtml`, `Edit.cshtml`
- Create: `src/EduConnect.Web/Views/Announcement/_TargetPicker.cshtml`

**Interfaces:**
- Consumes: `IAudienceService` (Tasks 3–5).
- Produces: `AnnouncementFormViewModel.TargetCollegeIDs/TargetDepartmentIDs/TargetProgramIDs` (`List<int>`), `AnnouncementFormViewModel.TargetOptions` (`TargetOptions`); private `AnnouncementController.PopulateAudienceAsync(AnnouncementFormViewModel model, int userID)`, `SaveTargets(Announcement, AnnouncementFormViewModel)`, `NotifyAsync(Announcement, int authorId)`.

- [ ] **Step 1: View model**

In `AnnouncementFormViewModel`, delete the `[MinLength(1, ErrorMessage = …)]` attribute (and its comment) above `SelectedTagIDs` — an announcement may now be tags-only, targets-only or both; the controller checks at least one. After `SelectedTagIDs` add:
```csharp

        // Hierarchy targets (IAudienceService validates them)
        public List<int> TargetCollegeIDs { get; set; } = new();
        public List<int> TargetDepartmentIDs { get; set; } = new();
        public List<int> TargetProgramIDs { get; set; } = new();

        [Microsoft.AspNetCore.Mvc.ModelBinding.Validation.ValidateNever]
        public EduConnect.Web.Services.TargetOptions TargetOptions { get; set; } = new();
```

- [ ] **Step 2: Controller plumbing**

Inject `IAudienceService audience` as the last constructor parameter of `AnnouncementController` (field `_audience`).

**First**, before adding any helper, route every tag reload through the new helper name:
```bash
cd /c/EduConnect/src/EduConnect.Web && sed -i 's/model\.AvailableTags = await GetSelectableTagsAsync(userID);/await PopulateAudienceAsync(model, userID);/' Controllers/AnnouncementController.cs && grep -c "await PopulateAudienceAsync(model, userID);" Controllers/AnnouncementController.cs
```
Expected: `11`.

**Then** replace `GetSelectableTagsAsync` with the following (the helper's own `model.AvailableTags = …` line is written after the sed, so it is not rewritten):
```csharp
        // Tags a user may target: their own UserDepartments rows, except
        // tags that have become colleges (those are targeted through the
        // hierarchy now). Admins do not author announcements.
        private async Task<List<DepartmentTag>> GetSelectableTagsAsync(int userID)
        {
            var tagIDs = await GetUserTagIDsAsync(userID);
            return await _context.DepartmentTags
                .Include(d => d.TagType)
                .Where(d => d.IsActive && tagIDs.Contains(d.TagID) &&
                            !_context.Colleges.Any(c => c.LegacyTagID == d.TagID))
                .OrderBy(d => d.TagName)
                .ToListAsync();
        }

        private async Task PopulateAudienceAsync(AnnouncementFormViewModel model, int userID)
        {
            model.AvailableTags = await GetSelectableTagsAsync(userID);
            model.TargetOptions = await _audience.GetTargetOptionsAsync(userID);
        }

        private static TargetSelection SelectionOf(AnnouncementFormViewModel model) =>
            new(model.TargetCollegeIDs ?? new(), model.TargetDepartmentIDs ?? new(), model.TargetProgramIDs ?? new());

        private void SaveTargets(Announcement announcement, AnnouncementFormViewModel model)
        {
            foreach (var id in (model.TargetCollegeIDs ?? new()).Distinct())
                _context.AnnouncementTargets.Add(new AnnouncementTarget { AnnouncementID = announcement.AnnouncementID, CollegeID = id });
            foreach (var id in (model.TargetDepartmentIDs ?? new()).Distinct())
                _context.AnnouncementTargets.Add(new AnnouncementTarget { AnnouncementID = announcement.AnnouncementID, DepartmentID = id });
            foreach (var id in (model.TargetProgramIDs ?? new()).Distinct())
                _context.AnnouncementTargets.Add(new AnnouncementTarget { AnnouncementID = announcement.AnnouncementID, ProgramID = id });
        }

        private async Task NotifyAsync(Announcement announcement, int authorId)
        {
            var recipientIds = await _audience.GetRecipientIdsAsync(announcement.AnnouncementID, authorId);
            if (recipientIds.Count > 0)
                await _notificationService.SendToManyAsync(
                    recipientIds,
                    "Announcement",
                    $"New announcement: {announcement.Title}",
                    $"/Announcement/Details/{announcement.AnnouncementID}",
                    announcement.AnnouncementID);
        }
```

- [ ] **Step 3: Validation in Create and Edit POST**

In both Create POST and Edit POST, replace the tag-security block's allowed list `var allowedTagIDs = await GetUserTagIDsAsync(userID);` with
```csharp
                var allowedTagIDs = (await GetSelectableTagsAsync(userID)).Select(t => t.TagID).ToList();
```
and, immediately after that whole tag-security `if` block, insert:
```csharp
            var targetCheck = await _audience.ValidateTargetsAsync(userID, SelectionOf(model));
            if (!targetCheck.Ok)
                ModelState.AddModelError("SelectedTagIDs", targetCheck.Error!);

            if ((model.SelectedTagIDs == null || !model.SelectedTagIDs.Any()) &&
                SelectionOf(model).IsEmpty)
                ModelState.AddModelError("SelectedTagIDs",
                    "Choose at least one audience: a program, department, college or tag.");
```
In Create POST the unauthorized-tag branch returns early with its own error; keep it. Its model-level message `ModelState.AddModelError("", …)` changes to `ModelState.AddModelError("SelectedTagIDs", …)` in both actions so the picker shows it (the forms never rendered model-level errors).

- [ ] **Step 4: Save targets and notify in Create; replace in Edit; notify in Publish**

Create POST: replace the whole `// Save tags` block (its `if (model.SelectedTagIDs != null && model.SelectedTagIDs.Any()) { … }`) with this unconditional version, which also saves the targets:
```csharp
            // Save tags and targets
            foreach (var tagID in model.SelectedTagIDs ?? new List<int>())
            {
                _context.AnnouncementTags.Add(
                    new AnnouncementTag
                    {
                        AnnouncementID = announcement.AnnouncementID,
                        TagID = tagID,
                        CreatedAt = DateTime.Now
                    });
            }
            SaveTargets(announcement, model);
            await _context.SaveChangesAsync();
```
Replace the whole `// Send real-time notifications to department members` block (through its closing brace) with:
```csharp
            // Tags and targets decide reach. IsEmergency never widens it —
            // it only pins and badges the announcement for whoever they
            // already reach. School Wide ("ALL") is the campus-wide lever.
            if (announcement.Status == "Published")
                await NotifyAsync(announcement, userID);
```
Edit GET: add to the model initializer
```csharp
                TargetCollegeIDs = announcement.AnnouncementTargets.Where(t => t.CollegeID != null).Select(t => t.CollegeID!.Value).ToList(),
                TargetDepartmentIDs = announcement.AnnouncementTargets.Where(t => t.DepartmentID != null).Select(t => t.DepartmentID!.Value).ToList(),
                TargetProgramIDs = announcement.AnnouncementTargets.Where(t => t.ProgramID != null).Select(t => t.ProgramID!.Value).ToList(),
```
and add `.Include(a => a.AnnouncementTargets)` to its announcement query. Edit POST: add `.Include(a => a.AnnouncementTargets)` to its query; in `// ─── Replace tags ───` add after the tag `RemoveRange`:
```csharp
            _context.AnnouncementTargets.RemoveRange(announcement.AnnouncementTargets);
            SaveTargets(announcement, model);
```
Publish: remove `.Include(a => a.AnnouncementTags).ThenInclude(at => at.DepartmentTag)` from its query and replace the whole `// Notify department members` block with `await NotifyAsync(announcement, userID);`.

Details: after `Tags = announcement.AnnouncementTags.Select(at => at.DepartmentTag.TagName).ToList(),` build the model, then before `return View(...)` add:
```csharp
            foreach (var name in await _audience.GetTargetNamesAsync(announcement.AnnouncementID))
                if (!model.Tags.Contains(name))
                    model.Tags.Add(name);
```
(use the local variable name the action already uses for its `AnnouncementDetailViewModel`).

- [ ] **Step 5: Picker partial**

`src/EduConnect.Web/Views/Announcement/_TargetPicker.cshtml`:
```cshtml
@model EduConnect.Web.ViewModels.AnnouncementFormViewModel
@{
    var options = Model.TargetOptions;
    var college = options.College;
}

@if (college != null)
{
    <div class="mb-3" id="targetPicker">
        @if (options.CanTargetCollege)
        {
            <div class="form-check mb-2">
                <input class="form-check-input" type="checkbox" name="TargetCollegeIDs"
                       value="@college.CollegeID" id="tc_@college.CollegeID"
                       @(Model.TargetCollegeIDs.Contains(college.CollegeID) ? "checked" : "") />
                <label class="form-check-label fw-semibold" for="tc_@college.CollegeID">
                    Everyone in @college.Name
                </label>
            </div>
        }
        @foreach (var dept in college.Departments)
        {
            <div class="ms-2 mb-2">
                @if (options.CanTargetDepartments && !dept.IsImplicit)
                {
                    <div class="form-check">
                        <input class="form-check-input" type="checkbox" name="TargetDepartmentIDs"
                               value="@dept.DepartmentID" id="td_@dept.DepartmentID"
                               @(Model.TargetDepartmentIDs.Contains(dept.DepartmentID) ? "checked" : "") />
                        <label class="form-check-label" for="td_@dept.DepartmentID">
                            All of @dept.Name
                        </label>
                    </div>
                }
                else if (!dept.IsImplicit)
                {
                    <div class="small text-muted">@dept.Name</div>
                }
                @foreach (var program in dept.Programs)
                {
                    <div class="form-check ms-3">
                        <input class="form-check-input" type="checkbox" name="TargetProgramIDs"
                               value="@program.ProgramID" id="tp_@program.ProgramID"
                               @(Model.TargetProgramIDs.Contains(program.ProgramID) ? "checked" : "") />
                        <label class="form-check-label small" for="tp_@program.ProgramID">
                            @program.Name
                        </label>
                    </div>
                }
            </div>
        }
    </div>
    @if (Model.AvailableTags.Any())
    {
        <div class="small text-muted mb-2">Or tags:</div>
    }
}
else
{
    <p class="small text-muted mb-2">
        You are not placed in a college yet, so you can only post to the tags below.
        Ask your administrator to place you.
    </p>
}
```

- [ ] **Step 6: Use it in Create and Edit**

In both `Create.cshtml` and `Edit.cshtml`, in the `<!-- Department Tags -->` card:
- change the heading text `Target Departments` to `Audience`;
- insert `@await Html.PartialAsync("_TargetPicker", Model)` immediately after the `<span asp-validation-for="SelectedTagIDs" …></span>`;
- in the submit-guard script, change
  `var checked = form.querySelector('input[name="SelectedTagIDs"]:checked');`
  to
  `var checked = form.querySelector('input[name="SelectedTagIDs"]:checked, input[name^="Target"]:checked');`
  and its message to `'Choose at least one audience: a program, department, college or tag.'`.

- [ ] **Step 7: Build and verify**

Stop the server; build + test (`Build succeeded.`, `Passed: 107`).

Prepare: place `uitest.faculty` in CCIT → IT&IS as admin (Edit User), and confirm `uitest.student` is in BSIT.
1. As `uitest.faculty`, `/Announcement/Create` shows "Audience" with IT&IS's programs (BSIS, BSIT) as checkboxes, no college or department checkbox, and the School Wide tag below "Or tags:".
2. Create an announcement "Plan4 BSIT test" targeting BSIT only, save as draft (Faculty always drafts) → `AnnouncementTargets` has one row with BSIT's ProgramID.
3. Tamper: in the page, add `<input type=hidden name=TargetDepartmentIDs value=<IT&IS id>>` and submit a second one → form returns with "You can only post to your own department's programs." and nothing saved.
4. Approve & publish path is Plan 5's concern; to test notifications here, as admin run `UPDATE Announcements SET ApprovalStatus='Approved' WHERE Title='Plan4 BSIT test'`, then as `uitest.faculty` publish it from My Announcements → `SELECT UserID FROM Notifications WHERE AnnouncementID=<id>` returns `uitest.student`'s ID (and only students in BSIT).
5. Edit it, switch the target to BSIS → targets row now BSIS.
6. Clean up: delete the test announcement's Notifications, AnnouncementTargets, AnnouncementTags and the Announcement row; restore `uitest.faculty` to unplaced (`UPDATE Users SET CollegeID=NULL, DepartmentID=NULL WHERE UserID=17`).
Stop the server.

- [ ] **Step 8: Commit**

```bash
cd /c/EduConnect && git add src && git commit -m "Let authors target colleges, departments and programs

Faculty pick programs of their own department, Chairpersons their
department or its programs, Deans anything in their college. College
tags are no longer offered; they are targets now.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 7: Feeds and chatbot on the hierarchy

**Files:**
- Modify: `src/EduConnect.Web/Controllers/AnnouncementController.cs` (Index), `src/EduConnect.Web/Controllers/HomeController.cs`, `src/EduConnect.Web/Services/FeedRankingService.cs`, `src/EduConnect.Web/Services/ChatbotService.cs`

**Interfaces:**
- Consumes: `IAudienceService.GetViewerAsync/VisibleTo/AddressedTo/Not/AddTargetLabelsAsync`.
- Produces: `IFeedRankingService.GetPersonalizedFeedAsync(Viewer viewer, string? searchQuery, string? filterFeedType)`.

- [ ] **Step 1: Announcement list**

In `AnnouncementController.Index`, replace from `bool seesAllDepartments =` through the end of its `if (!seesAllDepartments) { … }` block with:
```csharp
            // Administrators read every announcement; everyone else — Deans
            // and Chairpersons included — sees their own scope.
            if (roleName != RoleNames.Administrator)
            {
                var viewer = await _audience.GetViewerAsync(userID);
                query = query.Where(_audience.VisibleTo(viewer));
            }
```
After the page's `announcements` list is materialized (`.ToListAsync();` of the `Select(a => new AnnouncementTableViewModel …)`), add `await _audience.AddTargetLabelsAsync(announcements);`.

- [ ] **Step 2: Feed service**

In `FeedRankingService`:
- Change the interface method and implementation signature to `Task<PersonalizedFeed> GetPersonalizedFeedAsync(Viewer viewer, string? searchQuery, string? filterFeedType)`; add a constructor parameter `IAudienceService audience` (field `_audience`); use `var userID = viewer.UserID;` where `userID` was used.
- Replace the `baseQuery` `Where` clause's tag condition so it reads:
  ```csharp
                .Where(a => a.Status == "Published" &&
                            (a.ExpiresAt == null || a.ExpiresAt > DateTime.Now))
                .Where(_audience.VisibleTo(viewer));
  ```
- Replace the Department-section filter `a.AnnouncementTags.Any(at => userTagIDs.Contains(at.TagID)) &&` with membership in an ID set computed in the database, added just before `var deptCutoff`:
  ```csharp
            var addressedIDs = (await baseQuery
                .Where(_audience.AddressedTo(viewer))
                .Select(a => a.AnnouncementID)
                .ToListAsync()).ToHashSet();
  ```
  and the filter becomes `addressedIDs.Contains(a.AnnouncementID) &&`.
- In `exploreBase`, replace the two negated tag conditions with `.Where(AudienceService.Not(_audience.VisibleTo(viewer)))` appended after the existing `Where` (keep `!a.IsEmergency` and its comment).
- At the end, before `return result;`, add:
  ```csharp
            await _audience.AddTargetLabelsAsync(
                result.Department.Concat(result.ForYou).Concat(result.Explore));
  ```

- [ ] **Step 3: Home dashboard**

In `HomeController`, inject `IAudienceService audience` (field `_audience`). In the Student branch replace the `userTagIDs` query and the feed call with:
```csharp
                var viewer = await _audience.GetViewerAsync(userID);
                var feed = await _feedRanking.GetPersonalizedFeedAsync(
                    viewer, searchQuery, filterFeedType);
```
Replace the fail-closed block (`var scopedTagIDs = …` through its `query = query.Where(…)`) with:
```csharp
            var scopedViewer = await _audience.GetViewerAsync(userID);
            query = query.Where(_audience.VisibleTo(scopedViewer));
```
and after `model.RecentAnnouncements = await query … .ToListAsync();` add `await _audience.AddTargetLabelsAsync(model.RecentAnnouncements);`.

- [ ] **Step 4: Chatbot**

In `ChatbotService`, inject `IAudienceService audience` (field `_audience`). In `BuildVisibleAnnouncementsQueryAsync` replace
```csharp
            var scopeToDepartment = forceDepartmentScope ||
                roleName is RoleNames.Student or RoleNames.StudentPending or RoleNames.Faculty or RoleNames.Staff;

            if (scopeToDepartment)
            {
                var userTagIDs = await GetUserTagIDsAsync(userId);

                query = query.Where(a =>
                    a.AnnouncementTags.Any(at => userTagIDs.Contains(at.TagID)) ||
                    a.AnnouncementTags.Any(at => at.DepartmentTag.ShortName == "ALL"));
            }
```
with
```csharp
            // Same scope as the announcement list: everyone but the
            // Administrator is limited to what reaches them.
            if (forceDepartmentScope || roleName != RoleNames.Administrator)
            {
                var viewer = await _audience.GetViewerAsync(userId);
                query = query.Where(_audience.VisibleTo(viewer));
            }
```
Delete the now-unused `GetUserTagIDsAsync`. Replace `GetUserDepartmentNamesAsync` with:
```csharp
        private async Task<List<string>> GetUserDepartmentNamesAsync(int userId)
        {
            var placement = await _context.Users
                .Where(u => u.UserID == userId)
                .Select(u => new
                {
                    Program = u.AcademicProgram == null ? null : u.AcademicProgram.Name,
                    Department = u.Department == null || u.Department.IsImplicit ? null : u.Department.Name,
                    College = u.College == null ? null : u.College.Name
                })
                .FirstOrDefaultAsync();

            // Office tags (and School Wide) still describe non-academic users.
            var tags = await _context.UserDepartments
                .Where(ud => ud.UserID == userId &&
                             !_context.Colleges.Any(c => c.LegacyTagID == ud.TagID))
                .Select(ud => ud.DepartmentTag.TagName)
                .ToListAsync();

            return new[] { placement?.Program, placement?.Department, placement?.College }
                .Where(s => !string.IsNullOrEmpty(s))
                .Select(s => s!)
                .Concat(tags)
                .ToList();
        }
```

- [ ] **Step 5: Build, test, verify**

Stop the server; build + test (`Build succeeded.`, `Passed: 107`). Fix any test constructor breakage in `AccountControllerTests` only if the build shows it (none expected).

Start the server:
1. As `uitest.student` (BSIT): `/` shows the dashboard sections without errors; `/Announcement` lists School Wide and CCIT announcements (compare with `sqlcmd`: every listed title is School Wide, CCIT-targeted, or targeted to IT&IS/BSIT); CCIT rows show a `CCIT` badge.
2. As admin: `/Announcement` still lists everything.
3. Check the server log (`preview_logs` level error) is clean.
Stop the server.

- [ ] **Step 6: Commit**

```bash
cd /c/EduConnect && git add src && git commit -m "Scope feeds and the chatbot by the hierarchy

Deans and Chairpersons now see their own college or department, not
every department.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 8: Dean and Faculty dashboards

**Files:**
- Modify: `src/EduConnect.Web/Controllers/DeanController.cs` (Index), `src/EduConnect.Web/Controllers/FacultyController.cs` (Index header), `src/CLAUDE.md`

**Interfaces:**
- Consumes: `IAudienceService.GetViewerAsync/AddressedTo`.

- [ ] **Step 1: Dean dashboard**

Inject `IAudienceService audience` into `DeanController`. In `Index`, replace everything from `// Get dean's department` down to (not including) `// ─── Chart Data ───` with:
```csharp
            var viewer = await _audience.GetViewerAsync(userID);
            var addressed = _audience.AddressedTo(viewer);
            var isDean = viewer.RoleName == RoleNames.Dean;

            // A Dean's scope is the college, a Chairperson's the department.
            var scope = await _context.Users
                .Where(u => u.UserID == userID)
                .Select(u => new
                {
                    CollegeName = u.College == null ? null : u.College.Name,
                    CollegeShort = u.College == null ? null : u.College.ShortName,
                    DepartmentName = u.Department == null || u.Department.IsImplicit ? null : u.Department.Name,
                    DepartmentShort = u.Department == null || u.Department.IsImplicit ? null : u.Department.ShortName
                })
                .FirstAsync();

            ViewBag.DepartmentName = (isDean ? scope.CollegeName : scope.DepartmentName ?? scope.CollegeName)
                ?? "No placement";
            ViewBag.DepartmentShort = (isDean ? scope.CollegeShort : scope.DepartmentShort ?? scope.CollegeShort)
                ?? "—";

            ViewBag.TotalPublished = await _context.Announcements
                .Where(a => a.Status == "Published")
                .Where(addressed)
                .CountAsync();

            ViewBag.TotalFaculty = await _context.Users
                .Where(u => u.IsActive &&
                            u.Role.RoleName == RoleNames.Faculty &&
                            (isDean ? u.CollegeID == viewer.CollegeID && viewer.CollegeID != null
                                    : u.DepartmentID == viewer.DepartmentID && viewer.DepartmentID != null))
                .CountAsync();

            ViewBag.TodayAnnouncements = await _context.Announcements
                .Where(a => a.Status == "Published" &&
                            a.PublishedAt.HasValue &&
                            a.PublishedAt.Value.Date == DateTime.Today)
                .Where(addressed)
                .CountAsync();
```
In the chart's `ViewBag.MonthlyCount` replace `_context.Announcements.Count(a => … && a.AnnouncementTags.Any(at => at.TagID == deptTagID))` with `_context.Announcements.Where(addressed).Count(a => a.Status == "Published" && a.PublishedAt.HasValue && a.PublishedAt.Value.Month == m.Month && a.PublishedAt.Value.Year == m.Year)`, and in `ViewBag.RecentAnnouncements` replace the tag condition with `.Where(addressed)` after `.Where(a => a.Status == "Published")`.

- [ ] **Step 2: Faculty dashboard header**

In `FacultyController.Index`, replace the `// Get faculty's department` block and the two `ViewBag.Department*` assignments with:
```csharp
            var placement = await _context.Users
                .Where(u => u.UserID == userID)
                .Select(u => new
                {
                    Name = u.Department != null && !u.Department.IsImplicit
                        ? u.Department.Name
                        : u.College != null ? u.College.Name : null,
                    Short = u.Department != null && !u.Department.IsImplicit
                        ? u.Department.ShortName
                        : u.College != null ? u.College.ShortName : null
                })
                .FirstAsync();

            ViewBag.DepartmentName = placement.Name ?? "No placement";
            ViewBag.DepartmentShort = placement.Short ?? "—";
```

- [ ] **Step 3: Docs**

In `src/CLAUDE.md`, replace the **DepartmentTags** paragraph with:
```markdown
**Audience.** Announcements target the academic hierarchy through `AnnouncementTargets` (one of College/Department/Program per row) and non-academic audiences through `AnnouncementTags` (`ALL` = School Wide, office tags). `IAudienceService` owns every audience rule: `VisibleTo`/`AddressedTo` (EF expressions used by the announcement list, the student dashboard, the chatbot and the Dean/Faculty dashboards), `GetRecipientIdsAsync` (notifications), and `GetTargetOptionsAsync`/`ValidateTargetsAsync` (Dean → own college, Chairperson → own department, Faculty → own department's programs). `Details` is deliberately unscoped; Explore shows other colleges' non-emergency announcements.
```

- [ ] **Step 4: Build and verify**

Stop the server; build + test (`Build succeeded.`, `Passed: 107`). Start the server; as `uitest.faculty` the Faculty dashboard header shows "No placement" (unplaced) — then as admin place them in IT&IS and reload: header shows "Department of Information Technology & Information Systems"; restore to unplaced. There is no Dean/Chairperson test login: check `/Dean` compiles by requesting it as admin (redirects to Login — `IsDean` gate) and rely on the build; note this in the report. Stop the server.

- [ ] **Step 5: Commit**

```bash
cd /c/EduConnect && git add src && git commit -m "Show Dean and Faculty dashboards for their college or department

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

## After this plan

Report: tests passing, what each role can now target and see, the migration to apply on Azure (`AddAnnouncementTargets`, with its backfill), and that approval routing still uses tags until Plan 5. Update the project memory note on department-tag semantics (Explore and unscoped Details still hold; Deans/Chairpersons are now scoped). Then write Plan 5 (department-based approval routing, escalate-to-Dean toggles, in-flight reset, event access on placement, retiring academic tags, docs and `database/EduConnectDB.sql`).
