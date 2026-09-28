# Student Registration & Profile Program Implementation Plan (Plan 3 of 5)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Students pick their College → Department → Program when registering (required), can change it from their profile, and any verified student who still has no program is sent to their profile to choose one before using the app.

**Architecture:** The role-aware placement picker from Plan 2 moves to `Views/Shared` behind a small `IPlacementForm` interface so the admin forms, registration and profile all render the same cascade. `PlacementService` stays the only writer of placement and gains `SyncFeedTagAsync`, a temporary bridge that keeps a student's primary `UserDepartments` tag pointing at their college's legacy tag, because the feed still reads tags until Plan 4. A static `ProgramCompletion` policy decides when a request must be redirected; a thin middleware applies it using a session flag set at login and cleared once the student is placed.

**Tech Stack:** ASP.NET Core 8 MVC, EF Core 8, xUnit + SQLite in-memory, Razor, vanilla JS.

**Spec:** `docs/superpowers/specs/2026-09-28-college-hierarchy-and-roles-design.md`

## Global Constraints

- Program is required at registration; college and department are derived by `PlacementService` (most specific level wins).
- Student profile program changes apply immediately — no re-verification (spec open item, accepted).
- Only role `Student` is forced to complete a program (`Student Pending` cannot log in). The forced redirect applies to **GET requests that accept `text/html`** only, never to `fetch`/AJAX, SignalR, or POSTs, and never to `/Account/Profile`, `/Account/ChangePassword`, `/Account/Logout`, `/Account/Login`.
- Admin, Staff, Dean, Chairperson and Faculty see their placement read-only on the profile; only the admin changes it (Plan 2).
- `SyncFeedTagAsync` is temporary (removed in Plan 4/5); it only runs for students.
- Role comparisons use `RoleNames.*`. Never push `main`. Stop the dev server before building. Commits: sentence-case imperative, ending `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- Test command: `cd /c/EduConnect/src && dotnet test EduConnect.Tests` (plus `dotnet build EduConnect.Web` for view changes).
- Local test logins: `admin@educonnect.edu`, `uitest.faculty@educonnect.edu` (passwords in project memory). Task 5 creates `uitest.student@educonnect.edu` with the same password as `uitest.faculty`; add it to the project memory note.

## Review Focus

1. **A student switches to a college that has no legacy tag** (College of Education & Liberal Arts) — expected: their old college tag is removed, not left pointing at the previous college's feed. Test: `SyncFeedTag_CollegeWithoutLegacyTag_RemovesPrimaryTag` (Task 2).
2. **A student already holds a non-primary tag row for their new college** — expected: that row is promoted, not duplicated (the `(UserID, TagID)` unique index would throw). Test: `SyncFeedTag_ExistingNonPrimaryRowForNewTag_PromotesIt` (Task 2).
3. **Background `fetch` calls (notification count, chatbot) from a student who needs a program** — expected: served normally, never redirected to an HTML page. Test: `ShouldRedirect_FetchWithoutHtmlAccept_False` (Task 5).
4. **A student placed by the admin while logged in** — expected: the next Profile visit notices and stops redirecting. Covered in Task 5's browser check (Profile GET recomputes the flag).
5. **Registration submitted without choosing a program, or with a retired program** — expected: form returns with the placement error and the user's other entries kept, nothing saved. Covered in Task 3's browser check and `Student_RetiredProgram_Fails` (Plan 2).

---

## File Structure

| File | Responsibility |
|---|---|
| `src/EduConnect.Web/ViewModel/IPlacementForm.cs` (create) | What the placement picker needs from a view model |
| `src/EduConnect.Web/Views/Shared/_PlacementFields.cshtml` (move from `Views/Admin/`) | Shared cascade picker; student-only mode when the page has no role select |
| `src/EduConnect.Web/Services/IPlacementService.cs`, `PlacementService.cs` (modify) | `SyncFeedTagAsync` |
| `src/EduConnect.Web/Services/ProgramCompletion.cs` (create) | Forced-completion policy (pure functions) |
| `src/EduConnect.Web/ViewModel/{AdminUserFormViewModel,RegisterViewModel,ProfileViewModel}.cs` (modify) | Implement `IPlacementForm` |
| `src/EduConnect.Web/Controllers/AccountController.cs` (modify) | Register, Profile, Login flag |
| `src/EduConnect.Web/Views/Account/{Register,Profile}.cshtml` (modify) | Picker / read-only placement / completion notice |
| `src/EduConnect.Web/Program.cs` (modify) | Completion middleware |
| `src/EduConnect.Tests/{TestDb,PlacementServiceTests,ProgramCompletionTests}.cs` | Tests |
| `src/CLAUDE.md` (modify) | Registration lifecycle |

---

### Task 1: Share the placement picker

**Files:**
- Create: `src/EduConnect.Web/ViewModel/IPlacementForm.cs`
- Move: `src/EduConnect.Web/Views/Admin/_PlacementFields.cshtml` → `src/EduConnect.Web/Views/Shared/_PlacementFields.cshtml`
- Modify: `src/EduConnect.Web/ViewModel/AdminUserFormViewModel.cs`

**Interfaces:**
- Produces: `interface IPlacementForm { int? CollegeID; int? DepartmentID; int? ProgramID; List<College> Hierarchy; }` (namespace `EduConnect.Web.ViewModels`, all `{ get; set; }`). The partial renders student-only when the page has no `#RoleID` select.

- [ ] **Step 1: Create the interface**

`src/EduConnect.Web/ViewModel/IPlacementForm.cs`:
```csharp
using EduConnect.Web.Models;

namespace EduConnect.Web.ViewModels
{
    // What _PlacementFields needs: the chosen levels and the active tree.
    public interface IPlacementForm
    {
        int? CollegeID { get; set; }
        int? DepartmentID { get; set; }
        int? ProgramID { get; set; }
        List<College> Hierarchy { get; set; }
    }
}
```
Make `AdminUserFormViewModel` implement it: change `public class AdminUserFormViewModel` to `public class AdminUserFormViewModel : IPlacementForm` (its properties already match).

- [ ] **Step 2: Move and generalize the partial**

```bash
cd /c/EduConnect && git mv src/EduConnect.Web/Views/Admin/_PlacementFields.cshtml src/EduConnect.Web/Views/Shared/_PlacementFields.cshtml
```
In the moved file:
- Change the first line to `@model EduConnect.Web.ViewModels.IPlacementForm`.
- Replace
  ```js
          function roleName() {
              const opt = roleSelect.options[roleSelect.selectedIndex];
              return opt ? opt.text.trim() : '';
          }
  ```
  with
  ```js
          // Registration and the student profile have no role select:
          // the picker is then always for a student.
          function roleName() {
              if (!roleSelect) return roles.student;
              const opt = roleSelect.options[roleSelect.selectedIndex];
              return opt ? opt.text.trim() : '';
          }
  ```
- Replace `if (!roleSelect.value) {` with `if (roleSelect && !roleSelect.value) {`.
- Replace `hint.textContent = 'Choose the student\'s program.';` with `hint.textContent = roleSelect ? 'Choose the student\'s program.' : 'Choose your college, department and program.';`
- Replace `roleSelect.addEventListener('change', refresh);` with `if (roleSelect) roleSelect.addEventListener('change', refresh);`

The admin views' `@await Html.PartialAsync("_PlacementFields", Model)` calls resolve the Shared location unchanged.

- [ ] **Step 3: Build and verify the admin forms still work**

Stop the server; `cd /c/EduConnect/src && dotnet build EduConnect.Web 2>&1 | grep -E " error |Build succeeded" | sort -u && dotnet test EduConnect.Tests 2>&1 | tail -1` → `Build succeeded.`, `Passed! … Passed: 48`.

Start the server, log in as admin, open `/Admin/EditUser/17` and `/Admin/AddUser`: the Placement row renders; on EditUser the Faculty role shows College + Department with hint "Choose the department."; on AddUser with no role chosen the hint reads "Choose a role first." Stop the server.

- [ ] **Step 4: Commit**

```bash
cd /c/EduConnect && git add -A src/EduConnect.Web && git commit -m "Share the placement picker beyond the admin forms

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: Keep a student's feed tag in step with their college

**Files:**
- Modify: `src/EduConnect.Web/Services/IPlacementService.cs`, `src/EduConnect.Web/Services/PlacementService.cs`
- Modify: `src/EduConnect.Tests/TestDb.cs`
- Test: `src/EduConnect.Tests/PlacementServiceTests.cs`

**Interfaces:**
- Consumes: `User.CollegeID` set by `ApplyAsync`; `College.LegacyTagID`.
- Produces: `IPlacementService.SyncFeedTagAsync(User user) : Task` — requires `user.UserID` to exist in the database; stages changes, does not save. `TestDb.AddTag(string shortName) : DepartmentTag`. Student error message becomes `"Choose a program."`.

- [ ] **Step 1: Add the tag helper to TestDb**

In `TestDb`, after `AddProgram`, add:
```csharp
        public DepartmentTag AddTag(string shortName)
        {
            var type = Context.TagTypes.FirstOrDefault(t => t.TypeName == "Academic");
            if (type == null)
            {
                type = new TagType { TypeName = "Academic", Description = "Academic" };
                Context.TagTypes.Add(type);
                Context.SaveChanges();
            }

            var tag = new DepartmentTag
            {
                TagName = shortName + " tag",
                ShortName = shortName,
                TagTypeID = type.TagTypeID,
                ColorHex = "#000000"
            };
            Context.DepartmentTags.Add(tag);
            Context.SaveChanges();
            return tag;
        }
```

- [ ] **Step 2: Write the failing tests**

Append inside `PlacementServiceTests`:
```csharp
        private List<(int TagID, bool IsPrimary)> TagsOf(int userId) =>
            _db.NewContext().UserDepartments
                .Where(ud => ud.UserID == userId)
                .Select(ud => new { ud.TagID, ud.IsPrimary })
                .AsEnumerable()
                .Select(x => (x.TagID, x.IsPrimary))
                .OrderBy(x => x.TagID)
                .ToList();

        private College CollegeWithTag(string name, string shortName)
        {
            var tag = _db.AddTag(shortName);
            var college = _db.AddCollege(name);
            college.LegacyTagID = tag.TagID;
            _db.Context.SaveChanges();
            return college;
        }

        [Fact]
        public async Task SyncFeedTag_NewStudent_AddsCollegeTagAsPrimary()
        {
            var cba = CollegeWithTag("CBA", "CBA");
            var user = _db.AddUser(RoleNames.Student, cba.CollegeID);

            await Service.SyncFeedTagAsync(user);
            _db.Context.SaveChanges();

            Assert.Equal(new[] { (cba.LegacyTagID!.Value, true) }, TagsOf(user.UserID));
        }

        [Fact]
        public async Task SyncFeedTag_ChangedCollege_ReplacesPrimaryTag()
        {
            var cba = CollegeWithTag("CBA", "CBA");
            var coe = CollegeWithTag("COE", "COE");
            var user = _db.AddUser(RoleNames.Student, cba.CollegeID);
            await Service.SyncFeedTagAsync(user);
            _db.Context.SaveChanges();

            user.CollegeID = coe.CollegeID;
            await Service.SyncFeedTagAsync(user);
            _db.Context.SaveChanges();

            Assert.Equal(new[] { (coe.LegacyTagID!.Value, true) }, TagsOf(user.UserID));
        }

        [Fact]
        public async Task SyncFeedTag_CollegeWithoutLegacyTag_RemovesPrimaryTag()
        {
            var cba = CollegeWithTag("CBA", "CBA");
            var user = _db.AddUser(RoleNames.Student, cba.CollegeID);
            await Service.SyncFeedTagAsync(user);
            _db.Context.SaveChanges();

            user.CollegeID = _cos.CollegeID;   // _cos has no LegacyTagID
            await Service.SyncFeedTagAsync(user);
            _db.Context.SaveChanges();

            Assert.Empty(TagsOf(user.UserID));
        }

        [Fact]
        public async Task SyncFeedTag_ExistingNonPrimaryRowForNewTag_PromotesIt()
        {
            var cba = CollegeWithTag("CBA", "CBA");
            var coe = CollegeWithTag("COE", "COE");
            var user = _db.AddUser(RoleNames.Student, cba.CollegeID);
            _db.Context.UserDepartments.Add(new UserDepartment { UserID = user.UserID, TagID = cba.LegacyTagID!.Value, IsPrimary = true });
            _db.Context.UserDepartments.Add(new UserDepartment { UserID = user.UserID, TagID = coe.LegacyTagID!.Value, IsPrimary = false });
            _db.Context.SaveChanges();

            user.CollegeID = coe.CollegeID;
            await Service.SyncFeedTagAsync(user);
            _db.Context.SaveChanges();

            Assert.Equal(new[] { (coe.LegacyTagID!.Value, true) }, TagsOf(user.UserID));
        }

        [Fact]
        public async Task SyncFeedTag_AlreadyCorrect_LeavesRowAlone()
        {
            var cba = CollegeWithTag("CBA", "CBA");
            var user = _db.AddUser(RoleNames.Student, cba.CollegeID);
            await Service.SyncFeedTagAsync(user);
            _db.Context.SaveChanges();
            var before = _db.NewContext().UserDepartments.Single().UserDepartmentID;

            await Service.SyncFeedTagAsync(user);
            _db.Context.SaveChanges();

            Assert.Equal(before, _db.NewContext().UserDepartments.Single().UserDepartmentID);
        }
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `cd /c/EduConnect/src && dotnet test EduConnect.Tests 2>&1 | grep -E "error CS" | sort -u | head -2`
Expected: `'PlacementService' does not contain a definition for 'SyncFeedTagAsync'`.

- [ ] **Step 4: Implement**

In `IPlacementService` add:
```csharp

        // Temporary bridge until the feed reads placement (Plan 4): points
        // the student's primary UserDepartments tag at their college's
        // legacy tag, or removes it when the college has none. The user
        // must already be saved; stages changes, the caller saves.
        Task SyncFeedTagAsync(User user);
```
In `PlacementService`, change `"Choose the student's program."` to `"Choose a program."` and add:
```csharp
        public async Task SyncFeedTagAsync(User user)
        {
            int? tagId = null;
            if (user.CollegeID != null)
                tagId = await _context.Colleges
                    .Where(c => c.CollegeID == user.CollegeID)
                    .Select(c => c.LegacyTagID)
                    .FirstOrDefaultAsync();

            var rows = await _context.UserDepartments
                .Where(ud => ud.UserID == user.UserID)
                .ToListAsync();

            var primary = rows.FirstOrDefault(ud => ud.IsPrimary);
            if (primary?.TagID == tagId)
                return;

            if (primary != null)
                _context.UserDepartments.Remove(primary);

            if (tagId == null)
                return;

            // (UserID, TagID) is unique: promote a row that already exists.
            var existing = rows.FirstOrDefault(ud => ud.TagID == tagId && !ud.IsPrimary);
            if (existing != null)
                existing.IsPrimary = true;
            else
                _context.UserDepartments.Add(new UserDepartment
                {
                    UserID = user.UserID,
                    TagID = tagId.Value,
                    IsPrimary = true,
                    CreatedAt = DateTime.Now
                });
        }
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `cd /c/EduConnect/src && dotnet test EduConnect.Tests 2>&1 | tail -1`
Expected: `Passed!  - Failed: 0, Passed: 53`.

- [ ] **Step 6: Commit**

```bash
cd /c/EduConnect && git add src && git commit -m "Keep a student's feed tag in step with their college

The feed still reads UserDepartments tags until it moves onto the
hierarchy, so a student's primary tag now follows their college.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 3: Program picker at registration

**Files:**
- Modify: `src/EduConnect.Web/ViewModel/RegisterViewModel.cs`, `src/EduConnect.Web/Controllers/AccountController.cs` (ctor ~13-35, Register GET/POST ~138-296), `src/EduConnect.Web/Views/Account/Register.cshtml` (~161-186), `src/CLAUDE.md`

**Interfaces:**
- Consumes: `IPlacementForm`, `_PlacementFields` (Task 1); `IPlacementService.ApplyAsync`, `SyncFeedTagAsync` (Task 2); `IHierarchyService.GetTreeAsync`.
- Produces: `RegisterViewModel : IPlacementForm` without `DepartmentTagID`/`Departments`; `AccountController` fields `_hierarchy`, `_placement`.

- [ ] **Step 1: View model**

In `RegisterViewModel`, make the class `public class RegisterViewModel : IPlacementForm`, and replace
```csharp
        [Required(ErrorMessage = "Please select your department")]
        public int? DepartmentTagID { get; set; }

        // Only departments — no roles
        public List<DepartmentTag> Departments { get; set; }
            = new List<DepartmentTag>();
```
with
```csharp
        // College and department are derived from the program by
        // IPlacementService; only ProgramID is required.
        public int? CollegeID { get; set; }
        public int? DepartmentID { get; set; }
        public int? ProgramID { get; set; }

        [Microsoft.AspNetCore.Mvc.ModelBinding.Validation.ValidateNever]
        public List<College> Hierarchy { get; set; } = new();
```

- [ ] **Step 2: Controller**

Add `IHierarchyService hierarchy, IPlacementService placement` as the last two constructor parameters of `AccountController`, with fields `private readonly IHierarchyService _hierarchy;` / `private readonly IPlacementService _placement;` assigned in the constructor.

Replace the `Register()` GET body's model creation with:
```csharp
            var model = new RegisterViewModel
            {
                Hierarchy = await _hierarchy.GetTreeAsync(includeRetired: false)
            };

            return View(model);
```
In `Register(RegisterViewModel model)` POST, replace each of the three blocks that reload `model.Departments = await _context.DepartmentTags ... .ToListAsync();` with
```csharp
                model.Hierarchy = await _hierarchy.GetTreeAsync(includeRetired: false);
```
and add the same line before the `return View(model);` in the `pendingRole == null` branch.

Then, immediately after `var user = new User { ... };` (before `_context.Users.Add(user);`) insert:
```csharp
            var placement = await _placement.ApplyAsync(
                user, RoleNames.StudentPending,
                model.CollegeID, model.DepartmentID, model.ProgramID);
            if (!placement.Ok)
            {
                ModelState.AddModelError("Placement", placement.Error!);
                model.Hierarchy = await _hierarchy.GetTreeAsync(includeRetired: false);
                return View(model);
            }
```
and replace the whole `// Assign department` block (`if (model.DepartmentTagID.HasValue) { ... }`) with:
```csharp
            // The feed still reads tags (until Plan 4): give the new student
            // their college's tag.
            await _placement.SyncFeedTagAsync(user);
            await _context.SaveChangesAsync();
```

- [ ] **Step 3: View**

In `Register.cshtml`, replace the whole `<!-- Department -->` block (from `<!-- Department -->` through the closing `</div>` after `<span asp-validation-for="DepartmentTagID" …></span>`) with:
```cshtml
                        <!-- Program -->
                        <div class="mb-3 row g-0">
                            @await Html.PartialAsync("_PlacementFields", Model)
                        </div>
```

- [ ] **Step 4: Docs**

In `src/CLAUDE.md`, change step 1 of "Roles & User Lifecycle" to:
```markdown
1. Student registers (choosing college → department → program; program required) → `VerificationStatus = "Pending"`, `RoleID = "Student Pending"`, `IsActive = false`
```

- [ ] **Step 5: Build and verify**

Stop the server; build + test (`Build succeeded.`, `Passed: 53`). Start the server; logged out, open `/Account/Register`:
1. The Program row shows College / Department / Program selects with hint "Choose your college, department and program."; picking CCIT limits Department to CS and IT&IS; College of Law hides Department.
2. Fill every other field (email `uitest.register@educonnect.edu`, student ID `TEST-0001`, any 8+ char password) but leave Program empty; submit via `form.submit()` → the page returns with "Choose a program." under the picker, other fields kept.
3. Choose CCIT → IT&IS → BS in Information Technology and submit → redirected to Login with the pending message.
4. Verify:
   ```bash
   sqlcmd -S 'localhost\SQLEXPRESS' -d EduConnectDB -E -C -W -Q "SET NOCOUNT ON; SELECT u.UserID, r.RoleName, c.ShortName, p.ShortName prog, t.ShortName tag FROM Users u JOIN Roles r ON r.RoleID=u.RoleID LEFT JOIN Colleges c ON c.CollegeID=u.CollegeID LEFT JOIN Programs p ON p.ProgramID=u.ProgramID LEFT JOIN UserDepartments ud ON ud.UserID=u.UserID AND ud.IsPrimary=1 LEFT JOIN DepartmentTags t ON t.TagID=ud.TagID WHERE u.Email='uitest.register@educonnect.edu'"
   ```
   Expected: `Student Pending | CCIT | BSIT | CCIT`.
5. Delete the test registrant and the admin notification it triggered (register it with first name `UITest`, last name `Register`):
   ```bash
   sqlcmd -S 'localhost\SQLEXPRESS' -d EduConnectDB -E -C -b -Q "DECLARE @id INT = (SELECT UserID FROM Users WHERE Email='uitest.register@educonnect.edu'); DELETE FROM Notifications WHERE Type='NewPendingStudent' AND Message LIKE '%UITest Register%'; DELETE FROM UserDepartments WHERE UserID=@id; DELETE FROM Users WHERE UserID=@id;"
   ```
Stop the server.

- [ ] **Step 6: Commit**

```bash
cd /c/EduConnect && git add src && git commit -m "Require a program when a student registers

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 4: Program on the profile

**Files:**
- Modify: `src/EduConnect.Web/ViewModel/ProfileViewModel.cs`, `src/EduConnect.Web/Controllers/AccountController.cs` (Profile GET ~336-362, POST ~366-470), `src/EduConnect.Web/Views/Account/Profile.cshtml` (~101-110)

**Interfaces:**
- Consumes: `IPlacementForm`, `_PlacementFields`, `IPlacementService` (Tasks 1–2).
- Produces: `ProfileViewModel : IPlacementForm` with `bool CanEditProgram`, `string? PlacementText`; private `AccountController.FillPlacementAsync(ProfileViewModel model, User user)`.

- [ ] **Step 1: View model**

Make `ProfileViewModel` implement `IPlacementForm` and add:
```csharp

        // Placement. Students edit it here; everyone else sees it read-only
        // (the admin places staff).
        public int? CollegeID { get; set; }
        public int? DepartmentID { get; set; }
        public int? ProgramID { get; set; }
        [ValidateNever]
        public List<EduConnect.Web.Models.College> Hierarchy { get; set; } = new();
        [ValidateNever]
        public bool CanEditProgram { get; set; }
        [ValidateNever]
        public string? PlacementText { get; set; }
```

- [ ] **Step 2: Controller helper**

Add to `AccountController`, above `// ─── GET: /Account/Profile`:
```csharp
        // Placement fields for the profile page. Loads the tree only for
        // students, who are the only ones who edit it here.
        private async Task FillPlacementAsync(ProfileViewModel model, User user)
        {
            model.CanEditProgram = user.Role.RoleName == RoleNames.Student;

            var parts = await _context.Users
                .Where(u => u.UserID == user.UserID)
                .Select(u => new
                {
                    Program = u.AcademicProgram == null ? null : u.AcademicProgram.Name,
                    Department = u.Department == null || u.Department.IsImplicit ? null : u.Department.Name,
                    College = u.College == null ? null : u.College.Name
                })
                .FirstAsync();
            var text = string.Join(" · ", new[] { parts.Program, parts.Department, parts.College }
                .Where(s => !string.IsNullOrEmpty(s)));
            model.PlacementText = text.Length > 0 ? text : null;

            if (model.CanEditProgram)
                model.Hierarchy = await _hierarchy.GetTreeAsync(includeRetired: false);
        }
```

- [ ] **Step 3: Profile GET**

In `Profile()` GET, add to the model initializer:
```csharp
                CollegeID = user.CollegeID,
                DepartmentID = user.DepartmentID,
                ProgramID = user.ProgramID,
```
and before `return View(model);` add `await FillPlacementAsync(model, user);`.

- [ ] **Step 4: Profile POST**

In `Profile(ProfileViewModel model)` POST, immediately before the `if (!ModelState.IsValid)` block that re-populates the read-only fields, insert:
```csharp
            // Students choose their own program; changes apply at once.
            if (user.Role.RoleName == RoleNames.Student)
            {
                var placement = await _placement.ApplyAsync(
                    user, RoleNames.Student,
                    model.CollegeID, model.DepartmentID, model.ProgramID);
                if (!placement.Ok)
                    ModelState.AddModelError("Placement", placement.Error!);
                else
                    await _placement.SyncFeedTagAsync(user);
            }
```
Inside that `if (!ModelState.IsValid)` block, before its `return View(model);`, add `await FillPlacementAsync(model, user);`.

- [ ] **Step 5: View**

In `Profile.cshtml`, immediately after the Role read-only block (the `<div class="mb-3">` containing `value="@Model.RoleName"`), insert:
```cshtml
                    <!-- Placement -->
                    @if (Model.CanEditProgram)
                    {
                        <div class="mb-3 row g-0">
                            @await Html.PartialAsync("_PlacementFields", Model)
                        </div>
                    }
                    else if (Model.PlacementText != null)
                    {
                        <div class="mb-3">
                            <label class="form-label fw-semibold">Placement</label>
                            <input type="text" class="form-control" value="@Model.PlacementText" disabled />
                            <div class="form-text">Your administrator manages this.</div>
                        </div>
                    }
```

- [ ] **Step 6: Build and verify**

Stop the server; build + test (`Build succeeded.`, `Passed: 53`).

Create the test student (Global Constraints) as admin through `/Admin/AddUser`: name UITest Student, email `uitest.student@educonnect.edu`, the uitest.faculty password, Role Student, feed tag CCIT, Placement CCIT → IT&IS → BS in Information Technology. Record the login in the project memory note `educonnect-ui-stack-constraints.md` next to the uitest.faculty line.

1. Log in as `uitest.student`; `/Account/Profile` shows the picker pre-selected on CCIT / IT&IS / BSIT.
2. Change to College of Science → Department of Biology → BS in Biology, Save → "Profile updated successfully."; sqlcmd shows the user's college COS, program BSBio, primary tag COS.
3. Log in as `uitest.faculty` (Faculty, currently unplaced) → Profile shows no Placement row. Log in as admin, place uitest.faculty in CCIT → IT&IS, then as uitest.faculty the Profile shows read-only "Department of Information Technology & Information Systems · College of Computing and Information Technology". Restore uitest.faculty afterwards: `UPDATE Users SET CollegeID=NULL, DepartmentID=NULL WHERE UserID=17`.
Stop the server.

- [ ] **Step 7: Commit**

```bash
cd /c/EduConnect && git add src && git commit -m "Let students change their program from their profile

Everyone else sees their placement there read-only.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 5: Make unplaced students choose a program

**Files:**
- Create: `src/EduConnect.Web/Services/ProgramCompletion.cs`, `src/EduConnect.Tests/ProgramCompletionTests.cs`
- Modify: `src/EduConnect.Web/Program.cs` (after the legacy-role session middleware), `src/EduConnect.Web/Controllers/AccountController.cs` (Login session block ~120-135; Profile GET/POST), `src/EduConnect.Web/Views/Account/Profile.cshtml` (top alerts)

**Interfaces:**
- Produces: `static class ProgramCompletion` (namespace `EduConnect.Web.Services`) with `const string SessionKey = "NeedsProgram"`, `const string ProfilePath = "/Account/Profile"`, `static bool NeedsProgram(string? roleName, int? programId)`, `static bool ShouldRedirect(string? sessionFlag, string method, string path, string? accept)`.

- [ ] **Step 1: Write the failing tests**

`src/EduConnect.Tests/ProgramCompletionTests.cs`:
```csharp
using EduConnect.Web;
using EduConnect.Web.Services;

namespace EduConnect.Tests
{
    public class ProgramCompletionTests
    {
        private const string Html = "text/html,application/xhtml+xml,*/*;q=0.8";

        [Fact]
        public void NeedsProgram_StudentWithoutProgram_True() =>
            Assert.True(ProgramCompletion.NeedsProgram(RoleNames.Student, null));

        [Fact]
        public void NeedsProgram_StudentWithProgram_False() =>
            Assert.False(ProgramCompletion.NeedsProgram(RoleNames.Student, 5));

        [Theory]
        [InlineData(RoleNames.Faculty)]
        [InlineData(RoleNames.Dean)]
        [InlineData(RoleNames.Administrator)]
        [InlineData(null)]
        public void NeedsProgram_OtherRoles_False(string? role) =>
            Assert.False(ProgramCompletion.NeedsProgram(role, null));

        [Fact]
        public void ShouldRedirect_FlaggedPageLoad_True() =>
            Assert.True(ProgramCompletion.ShouldRedirect("1", "GET", "/Home/Index", Html));

        [Fact]
        public void ShouldRedirect_NotFlagged_False() =>
            Assert.False(ProgramCompletion.ShouldRedirect(null, "GET", "/Home/Index", Html));

        [Fact]
        public void ShouldRedirect_FetchWithoutHtmlAccept_False() =>
            Assert.False(ProgramCompletion.ShouldRedirect("1", "GET", "/Notification/UnreadCount", "*/*"));

        [Fact]
        public void ShouldRedirect_Post_False() =>
            Assert.False(ProgramCompletion.ShouldRedirect("1", "POST", "/Home/Index", Html));

        [Theory]
        [InlineData("/Account/Profile")]
        [InlineData("/account/profile")]
        [InlineData("/Account/ChangePassword")]
        [InlineData("/Account/Logout")]
        [InlineData("/Account/Login")]
        public void ShouldRedirect_AllowedPages_False(string path) =>
            Assert.False(ProgramCompletion.ShouldRedirect("1", "GET", path, Html));
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `cd /c/EduConnect/src && dotnet test EduConnect.Tests 2>&1 | grep -E "error CS" | sort -u | head -2`
Expected: `The name 'ProgramCompletion' does not exist in the current context`.

- [ ] **Step 3: Implement the policy**

`src/EduConnect.Web/Services/ProgramCompletion.cs`:
```csharp
namespace EduConnect.Web.Services
{
    // A verified student with no program must choose one before using the
    // app. Login sets a session flag; a middleware sends their page loads
    // to the profile until it is cleared. Background fetches, SignalR and
    // form posts pass through untouched.
    public static class ProgramCompletion
    {
        public const string SessionKey = "NeedsProgram";
        public const string ProfilePath = "/Account/Profile";

        private static readonly string[] AllowedPaths =
        {
            "/Account/Profile",
            "/Account/ChangePassword",
            "/Account/Logout",
            "/Account/Login"
        };

        public static bool NeedsProgram(string? roleName, int? programId) =>
            roleName == RoleNames.Student && programId == null;

        public static bool ShouldRedirect(string? sessionFlag, string method, string path, string? accept) =>
            sessionFlag == "1" &&
            string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase) &&
            accept != null && accept.Contains("text/html", StringComparison.OrdinalIgnoreCase) &&
            !AllowedPaths.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase));
    }
}
```

- [ ] **Step 4: Run to verify they pass**

Run: `cd /c/EduConnect/src && dotnet test EduConnect.Tests 2>&1 | tail -1`
Expected: `Passed!  - Failed: 0, Passed: 68`.

- [ ] **Step 5: Wire it in**

In `Program.cs`, right after the middleware that clears legacy-role sessions (the `app.Use` block ending with `await next();` that follows `UseSession`), add:
```csharp
// A verified student without a program is sent to their profile to
// choose one (ProgramCompletion has the rules).
app.Use(async (context, next) =>
{
    if (EduConnect.Web.Services.ProgramCompletion.ShouldRedirect(
            context.Session.GetString(EduConnect.Web.Services.ProgramCompletion.SessionKey),
            context.Request.Method,
            context.Request.Path,
            context.Request.Headers.Accept.ToString()))
    {
        context.Response.Redirect(
            EduConnect.Web.Services.ProgramCompletion.ProfilePath + "?complete=1");
        return;
    }

    await next();
});
```
In `AccountController.Login` POST, after `HttpContext.Session.SetString("ProfilePicture", …);` add:
```csharp
            if (ProgramCompletion.NeedsProgram(user.Role.RoleName, user.ProgramID))
                HttpContext.Session.SetString(ProgramCompletion.SessionKey, "1");
```
In `Profile()` GET, before `return View(model);` add:
```csharp
            // Recomputed on every visit: the admin may have placed the
            // student since they logged in.
            if (ProgramCompletion.NeedsProgram(user.Role.RoleName, user.ProgramID))
                HttpContext.Session.SetString(ProgramCompletion.SessionKey, "1");
            else
                HttpContext.Session.Remove(ProgramCompletion.SessionKey);
            ViewBag.MustChooseProgram =
                ProgramCompletion.NeedsProgram(user.Role.RoleName, user.ProgramID);
```
In `Profile(ProfileViewModel model)` POST, right after `await _context.SaveChangesAsync();` (the successful save), add:
```csharp
            if (!ProgramCompletion.NeedsProgram(user.Role.RoleName, user.ProgramID))
                HttpContext.Session.Remove(ProgramCompletion.SessionKey);
```
and inside the `if (!ModelState.IsValid)` block add `ViewBag.MustChooseProgram = ProgramCompletion.NeedsProgram(user.Role.RoleName, user.ProgramID);` before `return View(model);`.

In `Profile.cshtml`, above the `@if (TempData["Success"] != null)` block, add:
```cshtml
@if (ViewBag.MustChooseProgram == true)
{
    <div class="alert alert-warning mb-4">
        <i class="bi bi-mortarboard me-2"></i>
        Choose your college, department and program below to continue using EduConnect.
    </div>
}
```

- [ ] **Step 6: Build and verify**

Stop the server; build + test (`Build succeeded.`, `Passed: 68`).

1. Unplace the test student: `sqlcmd … -Q "UPDATE Users SET CollegeID=NULL, DepartmentID=NULL, ProgramID=NULL WHERE Email='uitest.student@educonnect.edu'"`.
2. Start the server; log in as `uitest.student` → lands on `/Account/Profile?complete=1` with the warning banner. Navigating to `/Announcement` or `/` → back on the profile. In the page, `await fetch('/Notification/UnreadCount').then(r => r.status)` → `200` (not redirected).
3. Choose CCIT → IT&IS → BSIT and Save → success; `/Announcement` now loads normally.
4. Unplace again via sqlcmd (step 1), log in again (redirected), then as admin in another tab place the student in any program; the student's next page load still redirects to Profile once, which then shows no banner, and afterwards `/Announcement` loads (Review Focus 4).
5. Log in as `uitest.faculty` (Faculty, unplaced) → not redirected anywhere.
6. Leave `uitest.student` placed in CCIT → IT&IS → BSIT.
Stop the server.

- [ ] **Step 7: Commit**

```bash
cd /c/EduConnect && git add src && git commit -m "Send students without a program to their profile first

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

## After this plan

Report: tests passing, what students now see, how many local students still have no program (they will be prompted at next login), and that the feed still uses tags until Plan 4. Then write Plan 4 (targeting, audience resolver, feed/notifications/chatbot on the hierarchy).
