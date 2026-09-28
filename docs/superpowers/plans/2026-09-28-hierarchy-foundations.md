# Hierarchy Foundations Implementation Plan (Plan 1 of 5)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Lay the groundwork for the College > Department > Program restructure without changing how announcements flow: one source of truth for role names, the Chairperson rename, removal of the Organization Adviser role and admin-announcement dead code, deletion of the CLAS/COED/PE tags, the seeded hierarchy tables, and user placement columns backfilled at college level.

**Architecture:** Role names move into a `RoleNames` constants class so the rename is one value. The hierarchy is three new EF Core entities (`College`, `Department`, `AcademicProgram`) seeded by an idempotent SQL data migration keyed on names. `Users` gains nullable `CollegeID` / `DepartmentID` / `ProgramID`; only `CollegeID` is backfilled here (from the user's primary legacy tag via `Colleges.LegacyTagID`).

**Tech Stack:** ASP.NET Core 8 MVC, EF Core 8 (SQL Server Express locally, Azure SQL in prod), Razor, `dotnet-ef` tool, `sqlcmd` for verification.

**Spec:** `docs/superpowers/specs/2026-09-28-college-hierarchy-and-roles-design.md`

## Global Constraints

- Data migrations key on `RoleName` / `ShortName` / `Name`, never on identity IDs (they differ between local and Azure).
- Every data migration is idempotent (`IF NOT EXISTS` / `WHERE NOT EXISTS`).
- No hard deletes of hierarchy rows; retire = `IsActive = 0` + `RetiredAt`.
- The C# entity for a program is `AcademicProgram` (table `Programs`) — never a class named `Program`.
- Role display name is exactly `Chairperson` (one word).
- Never push `main` (push triggers the Azure deploy); the user pushes.
- Stop the dev server before `dotnet build` (the running exe locks the build output).
- Commit messages: sentence-case imperative, no type prefix (repo style, e.g. "Let an admin add, rename and retire departments"), ending with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- There is no automated test project yet (Plan 2 adds one); verification here is `dotnet build`, `grep`, `sqlcmd` assertions and a browser smoke check on port 5120.

## Review Focus

1. **A database where CLAS/COED/PE are still referenced** (users, announcements, orgs, study groups) — expect the migration to fail loudly on the Restrict FK, never to silently reassign. Task 5 adds a pre-check query.
2. **A Chairperson or ex-adviser logged in across the deploy** — their session still holds `Chair Person` / `Organization Adviser`; expect them to be sent to Login, not dropped onto the student feed. Task 2 adds the legacy-role session clear.
3. **Running the seed migration against a database that already has some hierarchy rows** — expect no duplicates and no error. Task 6 re-runs the seed SQL to prove it.
4. **Users whose primary tag is not a college** (Staff on `ALL`, office tags, admin with no tag) — expect `CollegeID` to stay NULL with no error. Task 7 asserts this.
5. **Organization Adviser users who advise an org** — after becoming Faculty they must still manage their org (OrgController gates on the Faculty role). Task 3 checks it in the browser.

---

## File Structure

| File | Responsibility |
|---|---|
| `src/EduConnect.Web/RoleNames.cs` (create) | Role name constants + legacy names that force a re-login |
| `src/EduConnect.Web/Models/College.cs` (create) | College entity |
| `src/EduConnect.Web/Models/Department.cs` (create) | Department entity (incl. implicit departments) |
| `src/EduConnect.Web/Models/AcademicProgram.cs` (create) | Program entity, table `Programs` |
| `src/EduConnect.Web/Models/User.cs` (modify) | Placement columns + navigations |
| `src/EduConnect.Web/Data/ApplicationDbContext.cs` (modify) | DbSets, keys, unique indexes, Restrict FKs |
| `src/EduConnect.Web/Program.cs` (modify) | Clear sessions that carry a legacy role name |
| `src/EduConnect.Web/Controllers/AnnouncementController.cs` (modify) | Drop admin branches; shared tag helpers |
| `src/EduConnect.Web/Controllers/AdminController.cs` (modify) | Drop stale `EMRG` system short name |
| `src/EduConnect.Web/Migrations/*` (create, 5 migrations) | Rename, adviser removal, tag deletion, hierarchy + seed, placement + backfill |
| Controllers / Services / Views listed in Task 1 (modify) | Use `RoleNames` instead of literals |
| `src/CLAUDE.md` (modify) | Role list and hierarchy note |

---

### Task 1: Role name constants

Behaviour-neutral: every role-name string literal outside `Migrations/` becomes a `RoleNames` constant. The `Chairperson` constant still holds `"Chair Person"` until Task 2.

**Files:**
- Create: `src/EduConnect.Web/RoleNames.cs`
- Modify (literal replacement): `Controllers/{Account,Admin,Announcement,Chatbot,Dean,Event,Faculty,Group,Home,Org,SafetyReport,Staff}Controller.cs`, `Services/ChatbotService.cs`, `Views/Announcement/{Create,Details,Edit,Index,Review,ReviewQueue}.cshtml`, `Views/Dean/Index.cshtml`, `Views/Event/Details.cshtml`, `Views/Group/{Details,Index}.cshtml`, `Views/Home/Index.cshtml`, `Views/Shared/_SidebarContent.cshtml`

**Interfaces:**
- Produces: `EduConnect.Web.RoleNames` with `const string Administrator, Dean, Chairperson, Faculty, Staff, Student, StudentPending` and `static readonly IReadOnlySet<string> Legacy`. Namespace `EduConnect.Web` so controllers, services and views (which already `@using EduConnect.Web`) see it without new usings.

- [ ] **Step 1: Record the baseline literal count**

Run (Git Bash, from `C:/EduConnect/src/EduConnect.Web`):
```bash
grep -rn -E '"(Administrator|Dean|Chair Person|Faculty|Staff|Student|Student Pending)"' --include=*.cs --include=*.cshtml . | grep -v Migrations/ | wc -l
```
Expected: a non-zero count (roughly 150 on 2026-09-28). Note it for the report.

- [ ] **Step 2: Create the constants class**

`src/EduConnect.Web/RoleNames.cs`:
```csharp
namespace EduConnect.Web
{
    // Every authorization check compares the session's RoleName string,
    // so these values must match Roles.RoleName exactly. Renaming a role
    // means a migration plus a change here — never a literal elsewhere.
    public static class RoleNames
    {
        public const string Administrator = "Administrator";
        public const string Dean = "Dean";
        public const string Chairperson = "Chair Person";
        public const string Faculty = "Faculty";
        public const string Staff = "Staff";
        public const string Student = "Student";
        public const string StudentPending = "Student Pending";

        // Role names that no longer exist. A session still carrying one
        // is cleared so the user logs in again and picks up the new name.
        public static readonly IReadOnlySet<string> Legacy =
            new HashSet<string>();
    }
}
```

- [ ] **Step 3: Replace the literals**

Run from `C:/EduConnect/src/EduConnect.Web`:
```bash
files=$(grep -rl -E '"(Administrator|Dean|Chair Person|Faculty|Staff|Student|Student Pending)"' --include=*.cs --include=*.cshtml . | grep -v Migrations/ | grep -v '^./RoleNames.cs$')
sed -i \
  -e 's/"Student Pending"/RoleNames.StudentPending/g' \
  -e 's/"Chair Person"/RoleNames.Chairperson/g' \
  -e 's/"Administrator"/RoleNames.Administrator/g' \
  -e 's/"Dean"/RoleNames.Dean/g' \
  -e 's/"Faculty"/RoleNames.Faculty/g' \
  -e 's/"Staff"/RoleNames.Staff/g' \
  -e 's/"Student"/RoleNames.Student/g' \
  $files
```

- [ ] **Step 4: Review the replacements that are not comparisons**

Run:
```bash
git diff -U0 | grep '^+' | grep -v -E '(==|!=|is |or |=> |Contains|\{ *RoleNames)' | grep RoleNames
```
Inspect each hit. Every one must be a role-name use (a `switch` arm, a HashSet initializer, a LINQ `r.RoleName == ...`). If any replaced literal is *display text* that happens to equal a role name (e.g. a heading `"Faculty"` in markup), revert that one line to the literal. In Razor, a replacement inside a C# expression (`@if (role == RoleNames.Student)`) is correct; a replacement inside plain HTML text would render the words `RoleNames.Student` — revert those.

- [ ] **Step 5: Build**

Stop the dev server if running, then:
```bash
cd /c/EduConnect/src && dotnet build EduConnect.Web 2>&1 | grep -E " error |Build succeeded"
```
Expected: `Build succeeded.` (Razor views compile at build time, so a bad view replacement fails here.)

- [ ] **Step 6: Verify no literals remain**

```bash
cd /c/EduConnect/src/EduConnect.Web && grep -rn -E '"(Administrator|Dean|Chair Person|Faculty|Staff|Student|Student Pending)"' --include=*.cs --include=*.cshtml . | grep -v Migrations/ | grep -v '^./RoleNames.cs'
```
Expected: no output, except any display-text lines you deliberately reverted in Step 4.

- [ ] **Step 7: Smoke test**

Start `educonnect-web` (port 5120) with the preview tool, log in as `admin@educonnect.edu` (password in the project memory / V2 seed) and confirm the admin dashboard loads; log out; log in as `uitest.faculty@educonnect.edu` and confirm the Faculty dashboard and sidebar render. Stop the server.

- [ ] **Step 8: Commit**

```bash
cd /c/EduConnect && git add src/EduConnect.Web && git commit -m "Keep role names in one place

Every role check compared a string literal; renaming a role meant
finding ~140 of them. They now read RoleNames constants.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: Rename Chair Person to Chairperson

**Files:**
- Create: `src/EduConnect.Web/Migrations/<timestamp>_RenameChairPersonToChairperson.cs` (+ Designer)
- Modify: `src/EduConnect.Web/RoleNames.cs`, `src/EduConnect.Web/Program.cs:139-155`, `Controllers/AnnouncementController.cs` (user-facing text), `Controllers/EventController.cs:71` (comment), `Views/Announcement/ReviewQueue.cshtml:12`, `Views/Dean/Index.cshtml:3`, `Views/Admin/Index.cshtml:122`, `src/CLAUDE.md`

**Interfaces:**
- Consumes: `RoleNames` (Task 1).
- Produces: `RoleNames.Chairperson == "Chairperson"`; `RoleNames.Legacy` contains `"Chair Person"`.

- [ ] **Step 1: Check the current database value**

```bash
sqlcmd -S 'localhost\SQLEXPRESS' -d EduConnectDB -E -C -W -h -1 -Q "SET NOCOUNT ON; SELECT RoleName FROM Roles WHERE RoleName LIKE 'Chair%'"
```
Expected: `Chair Person`.

- [ ] **Step 2: Scaffold an empty migration**

```bash
cd /c/EduConnect/src && dotnet ef migrations add RenameChairPersonToChairperson --project EduConnect.Web
```
Expected: a new migration with empty `Up`/`Down` (no model change).

- [ ] **Step 3: Fill the migration**

Replace the class body in the new `..._RenameChairPersonToChairperson.cs`:
```csharp
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Keyed on RoleName: RoleID is an identity column and differs
            // between databases. Guarded so a re-run is a no-op.
            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT 1 FROM Roles WHERE RoleName = 'Chairperson')
                UPDATE Roles
                SET RoleName = 'Chairperson',
                    Description = 'Reviews and publishes announcements for their department',
                    UpdatedAt = SYSDATETIME()
                WHERE RoleName = 'Chair Person';
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                UPDATE Roles
                SET RoleName = 'Chair Person',
                    Description = 'Can create announcements and events for their department',
                    UpdatedAt = SYSDATETIME()
                WHERE RoleName = 'Chairperson';
            ");
        }
```

- [ ] **Step 4: Change the constant and register the legacy name**

In `RoleNames.cs`:
```csharp
        public const string Chairperson = "Chairperson";
```
and
```csharp
        public static readonly IReadOnlySet<string> Legacy =
            new HashSet<string> { "Chair Person" };
```

- [ ] **Step 5: Clear sessions that carry a legacy role**

In `Program.cs`, inside the absolute-session-lifetime middleware, add this block immediately before the final `await next();` of that `app.Use` lambda:
```csharp
    // A role renamed or removed since this session logged in: the stored
    // RoleName now matches no check, so the user would silently fall
    // through to the student feed. Make them log in again instead.
    var sessionRole = context.Session.GetString("RoleName");
    if (sessionRole != null &&
        EduConnect.Web.RoleNames.Legacy.Contains(sessionRole))
    {
        context.Session.Clear();
        context.Response.Redirect("/Account/Login");
        return;
    }
```

- [ ] **Step 6: Update user-facing text**

Run from `C:/EduConnect/src/EduConnect.Web`:
```bash
sed -i 's/Chair Person/Chairperson/g' Controllers/AnnouncementController.cs Controllers/EventController.cs
sed -i 's/"Chair Person Queue"/"Chairperson Queue"/; ' Views/Announcement/ReviewQueue.cshtml
sed -i 's/"Chair Person Dashboard"/"Chairperson Dashboard"/' Views/Dean/Index.cshtml
sed -i 's#Dean / Chair<#Dean / Chairperson<#' Views/Admin/Index.cshtml
grep -rn "Chair Person" --include=*.cs --include=*.cshtml . | grep -v Migrations/ | grep -v RoleNames.cs
```
Expected final grep: no output. (The `AnnouncementController` sed only touches comments and message strings — the role literals were already constants after Task 1.)

- [ ] **Step 7: Update CLAUDE.md**

In `src/CLAUDE.md`, change the line
```
Named roles: `Administrator`, `Dean`, `Chair Person`, `Faculty`, `Staff`, `Student`, `Student Pending`
```
to
```
Named roles (constants in `RoleNames.cs` — never compare a literal): `Administrator`, `Dean`, `Chairperson`, `Faculty`, `Staff`, `Student`, `Student Pending`
```

- [ ] **Step 8: Build, apply, verify**

```bash
cd /c/EduConnect/src && dotnet build EduConnect.Web 2>&1 | grep -E " error |Build succeeded" && dotnet ef database update --project EduConnect.Web
sqlcmd -S 'localhost\SQLEXPRESS' -d EduConnectDB -E -C -W -h -1 -Q "SET NOCOUNT ON; SELECT RoleName FROM Roles WHERE RoleName LIKE 'Chair%'"
```
Expected: build succeeds; query prints `Chairperson`.

Re-run the Up SQL by hand to prove idempotency:
```bash
sqlcmd -S 'localhost\SQLEXPRESS' -d EduConnectDB -E -C -Q "IF NOT EXISTS (SELECT 1 FROM Roles WHERE RoleName = 'Chairperson') UPDATE Roles SET RoleName = 'Chairperson' WHERE RoleName = 'Chair Person'; SELECT COUNT(*) FROM Roles WHERE RoleName LIKE 'Chair%'"
```
Expected: count `1`.

- [ ] **Step 9: Browser check (Review Focus 2)**

Find a local Chairperson:
```bash
sqlcmd -S 'localhost\SQLEXPRESS' -d EduConnectDB -E -C -W -Q "SET NOCOUNT ON; SELECT u.Email FROM Users u JOIN Roles r ON r.RoleID=u.RoleID WHERE r.RoleName='Chairperson'"
```
Their passwords are not in the project. Ask the user for one test Chairperson login; never reset a real user's password yourself.
- **With a password:** Log in **before** applying this task's migration so the session holds `Chair Person`. Apply the migration and restart the server with the new code, then load any page. Expected: redirect to `/Account/Login`. Log in again. Expected: the "Chairperson Dashboard" title and the Chairperson sidebar section.
- **Without one:** Record "Chairperson login not verified: no test password" in the task report and continue.

Stop the server.

- [ ] **Step 10: Commit**

```bash
cd /c/EduConnect && git add src && git commit -m "Rename the Chair Person role to Chairperson

Sessions that still carry the old name are sent back to login rather
than falling through to the student feed.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 3: Remove the Organization Adviser role

`main` carries migration `20260921110455_AddOrganizationAdviserRole`, which inserts the role row. Advisers are Faculty with `OrgMembers.OrgRole = 'Adviser'`. Locally, three users hold the role (IDs 17, 18, 32) from a migration that exists in no branch.

**Files:**
- Create: `src/EduConnect.Web/Migrations/<timestamp>_RemoveOrganizationAdviserRole.cs` (+ Designer)
- Modify: `src/EduConnect.Web/RoleNames.cs`

**Interfaces:**
- Consumes: `RoleNames.Legacy` (Task 2).
- Produces: no `Organization Adviser` row in `Roles`; `RoleNames.Legacy` also contains `"Organization Adviser"`.

- [ ] **Step 1: Baseline**

```bash
sqlcmd -S 'localhost\SQLEXPRESS' -d EduConnectDB -E -C -W -Q "SET NOCOUNT ON; SELECT u.UserID, u.Email FROM Users u JOIN Roles r ON r.RoleID=u.RoleID WHERE r.RoleName='Organization Adviser'"
```
Expected: 3 rows (17, 18, 32).

- [ ] **Step 2: Scaffold and fill the migration**

```bash
cd /c/EduConnect/src && dotnet ef migrations add RemoveOrganizationAdviserRole --project EduConnect.Web
```
Class body:
```csharp
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // An organization adviser is a Faculty member whose OrgMembers
            // row says OrgRole = 'Adviser' — not a role of its own. Anyone
            // given the short-lived role goes back to Faculty (their
            // OrgMembers rows are untouched), then the row is removed.
            // Keyed on RoleName; RoleID differs between databases.
            migrationBuilder.Sql(@"
                IF EXISTS (SELECT 1 FROM Roles WHERE RoleName = 'Organization Adviser')
                BEGIN
                    UPDATE u
                    SET RoleID = f.RoleID, UpdatedAt = SYSDATETIME()
                    FROM Users u
                    JOIN Roles a ON a.RoleID = u.RoleID AND a.RoleName = 'Organization Adviser'
                    CROSS JOIN (SELECT RoleID FROM Roles WHERE RoleName = 'Faculty') f;

                    DELETE FROM Roles WHERE RoleName = 'Organization Adviser';
                END
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Restores the inert row only; converted users stay Faculty.
            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT 1 FROM Roles WHERE RoleName = 'Organization Adviser')
                INSERT INTO Roles
                    (RoleName, RoleLevel, Description, CanPublish, CanManageUsers, CreatedAt)
                VALUES
                    ('Organization Adviser', 2,
                     'Posts announcements for a single student organization',
                     0, 0, SYSDATETIME());
            ");
        }
```

- [ ] **Step 3: Register the legacy name**

In `RoleNames.cs`:
```csharp
        public static readonly IReadOnlySet<string> Legacy =
            new HashSet<string> { "Chair Person", "Organization Adviser" };
```

- [ ] **Step 4: Build, apply, verify**

```bash
cd /c/EduConnect/src && dotnet build EduConnect.Web 2>&1 | grep -E " error |Build succeeded" && dotnet ef database update --project EduConnect.Web
sqlcmd -S 'localhost\SQLEXPRESS' -d EduConnectDB -E -C -W -Q "SET NOCOUNT ON; SELECT COUNT(*) AS adviserRole FROM Roles WHERE RoleName='Organization Adviser'; SELECT u.UserID, r.RoleName FROM Users u JOIN Roles r ON r.RoleID=u.RoleID WHERE u.UserID IN (17,18,32)"
```
Expected: `adviserRole` = 0; users 17, 18, 32 show `Faculty`.

- [ ] **Step 5: Browser check (Review Focus 5)**

Start the server, log in as `uitest.faculty@educonnect.edu` (user 17; password in project memory). Open `/Org` and confirm the Faculty sidebar renders and any organization this user advises shows its Manage link. Stop the server.

- [ ] **Step 6: Commit**

```bash
cd /c/EduConnect && git add src && git commit -m "Drop the Organization Adviser role

An adviser is a Faculty member with the Adviser role inside the
organization. Anyone holding the role goes back to Faculty.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 4: Remove the admin announcement dead code

`CanCreate()` excludes Administrators and `CanEditAnnouncement()` only allows the author, so every `Administrator` branch in Create/Edit is unreachable. Replace them with two helpers.

**Files:**
- Modify: `src/EduConnect.Web/Controllers/AnnouncementController.cs` (Create GET/POST, Edit GET/POST)
- Modify: `src/EduConnect.Web/Views/Announcement/Create.cshtml:124`, `Views/Announcement/Edit.cshtml:158`

**Interfaces:**
- Produces (private, in `AnnouncementController`):
  - `Task<List<int>> GetUserTagIDsAsync(int userID)`
  - `Task<List<DepartmentTag>> GetSelectableTagsAsync(int userID)`

- [ ] **Step 1: Add the helpers**

Add below `CanSetEmergency()`:
```csharp
        // Tags a user may target: their own UserDepartments rows. Admins
        // do not author announcements (CanCreate excludes them), so there
        // is no "all tags" case.
        private Task<List<int>> GetUserTagIDsAsync(int userID) =>
            _context.UserDepartments
                .Where(ud => ud.UserID == userID)
                .Select(ud => ud.TagID)
                .ToListAsync();

        private async Task<List<DepartmentTag>> GetSelectableTagsAsync(int userID)
        {
            var tagIDs = await GetUserTagIDsAsync(userID);
            return await _context.DepartmentTags
                .Include(d => d.TagType)
                .Where(d => d.IsActive && tagIDs.Contains(d.TagID))
                .OrderBy(d => d.TagName)
                .ToListAsync();
        }
```

- [ ] **Step 2: Replace every admin branch**

List them:
```bash
cd /c/EduConnect/src/EduConnect.Web && grep -n -E 'RoleNames.Administrator|isAdmin' Controllers/AnnouncementController.cs
```
Handle each hit **except** the `seesAllDepartments` expression in `Index` (admins still read every department's feed — keep it):

- `if (roleName == RoleNames.Administrator) { ...all tags... } else { ...user tags... }` (Create GET, Edit GET) → replace the whole if/else with
  ```csharp
              model.AvailableTags = await GetSelectableTagsAsync(userID);
  ```
- `if (roleName != RoleNames.Administrator && model.SelectedTagIDs != null && ...)` (Create POST) and `if (!isAdmin && ...)` (Edit POST) → drop the first condition. Inside, replace the `allowedTagIDs` query with `var allowedTagIDs = await GetUserTagIDsAsync(userID);` and the `model.AvailableTags = ...` reload with `model.AvailableTags = await GetSelectableTagsAsync(userID);`.
- Every `var xxxIDs = roleName == RoleNames.Administrator ? ... : ...;` / `isAdmin ? ... : ...;` followed by `model.AvailableTags = await _context.DepartmentTags...Where(... xxxIDs.Contains(d.TagID))...;` → replace both statements with
  ```csharp
                      model.AvailableTags = await GetSelectableTagsAsync(userID);
  ```
- Delete `bool isAdmin = roleName == RoleNames.Administrator;` once unused.

- [ ] **Step 3: Fix the views**

In `Create.cshtml` and `Edit.cshtml` change
```csharp
                                c.CategoryName != "Emergency" || roleName == RoleNames.Administrator))
```
to
```csharp
                                c.CategoryName != "Emergency"))
```
If the view's `roleName` local is now unused, leave it — removing it risks other uses further down; the build will not warn on an unused Razor local.

- [ ] **Step 4: Verify**

```bash
cd /c/EduConnect/src/EduConnect.Web && grep -n -E 'RoleNames.Administrator|isAdmin' Controllers/AnnouncementController.cs Views/Announcement/Create.cshtml Views/Announcement/Edit.cshtml
cd /c/EduConnect/src && dotnet build EduConnect.Web 2>&1 | grep -E " error |Build succeeded"
```
Expected: only the `Index` `seesAllDepartments` line remains; build succeeds.

- [ ] **Step 5: Browser check**

Start the server, log in as `uitest.faculty@educonnect.edu`, open `/Announcement/Create`: the tag picker lists only that user's tags, the Emergency category card is absent, and submitting with no title shows validation with the picker still populated. Log in as admin and confirm `/Announcement/Create` redirects to `/Announcement`. Stop the server.

- [ ] **Step 6: Commit**

```bash
cd /c/EduConnect && git add src && git commit -m "Remove the unreachable admin announcement branches

Administrators cannot create announcements, so the all-tags pickers
were dead code. Create and Edit now share two small tag helpers.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 5: Delete the CLAS, COED and PE tags

**Files:**
- Create: `src/EduConnect.Web/Migrations/<timestamp>_RemoveLegacyCollegeTags.cs` (+ Designer)
- Modify: `src/EduConnect.Web/Controllers/AdminController.cs:763`

- [ ] **Step 1: Pre-check references (Review Focus 1)**

```bash
sqlcmd -S 'localhost\SQLEXPRESS' -d EduConnectDB -E -C -W -Q "SET NOCOUNT ON; SELECT t.ShortName, (SELECT COUNT(*) FROM UserDepartments x WHERE x.TagID=t.TagID) users, (SELECT COUNT(*) FROM AnnouncementTags x WHERE x.TagID=t.TagID) anns, (SELECT COUNT(*) FROM Organizations x WHERE x.DepartmentTagID=t.TagID) orgs, (SELECT COUNT(*) FROM StudyGroups x WHERE x.DepartmentTagID=t.TagID) sgs FROM DepartmentTags t WHERE t.ShortName IN ('CLAS','COED','PE')"
```
Expected locally: three rows, all zeros. **Before the user applies this migration to Azure they must run the same query there**; any non-zero count means the migration will fail on the FK and the data needs a decision first. Put this in the task report.

- [ ] **Step 2: Scaffold and fill the migration**

```bash
cd /c/EduConnect/src && dotnet ef migrations add RemoveLegacyCollegeTags --project EduConnect.Web
```
Class body:
```csharp
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // CLAS and COED are replaced by the College of Education &
            // Liberal Arts and the College of Science in the new
            // hierarchy; PE becomes a department. Every FK into
            // DepartmentTags is Restrict, so if anything still references
            // one of these rows this fails loudly instead of guessing.
            // Keyed on ShortName; TagID differs between databases.
            migrationBuilder.Sql(
                "DELETE FROM DepartmentTags WHERE ShortName IN ('CLAS', 'COED', 'PE');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                INSERT INTO DepartmentTags (TagName, ShortName, TagTypeID, Description, ColorHex, IsActive, CreatedAt)
                SELECT v.TagName, v.ShortName, tt.TagTypeID, v.Description, v.ColorHex, 1, SYSDATETIME()
                FROM (VALUES
                    ('College of Liberal Arts & Sciences', 'CLAS', 'Liberal Arts announcements', '#0369A1'),
                    ('College of Education', 'COED', 'Education department announcements', '#78350F'),
                    ('Physical Education Department', 'PE', 'PE department announcements', '#15803D')
                ) v(TagName, ShortName, Description, ColorHex)
                CROSS JOIN (SELECT TagTypeID FROM TagTypes WHERE TypeName = 'Academic') tt
                WHERE NOT EXISTS (SELECT 1 FROM DepartmentTags d WHERE d.ShortName = v.ShortName);
            ");
        }
```

- [ ] **Step 3: Drop the stale EMRG system code**

`main` removed the Emergency tag (`RemoveEmergencyDepartment`). In `AdminController.cs` change
```csharp
        private static readonly string[] SystemShortNames = { "ALL", "EMRG" };
```
to
```csharp
        private static readonly string[] SystemShortNames = { "ALL" };
```
Then update the comment above it (lines ~758-762) so it names only the `ALL` row, and check `Views/Admin/AddDepartment.cshtml`, `EditDepartment.cshtml`, `Departments.cshtml` and `ViewModel/AdminDepartmentFormViewModel.cs` for text mentioning `EMRG` / "Emergency" as a reserved code:
```bash
cd /c/EduConnect/src/EduConnect.Web && grep -n -i 'EMRG' Controllers/AdminController.cs Views/Admin/*.cshtml ViewModel/AdminDepartmentFormViewModel.cs
```
Edit each hit so it refers to `ALL` only. Expected after edits: no output.

- [ ] **Step 4: Build, apply, verify**

```bash
cd /c/EduConnect/src && dotnet build EduConnect.Web 2>&1 | grep -E " error |Build succeeded" && dotnet ef database update --project EduConnect.Web
sqlcmd -S 'localhost\SQLEXPRESS' -d EduConnectDB -E -C -W -h -1 -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM DepartmentTags WHERE ShortName IN ('CLAS','COED','PE')"
```
Expected: `0`.

- [ ] **Step 5: Browser check**

Start the server, log in as admin, open `/Admin/Departments`: CLAS/COED/PE are gone, `ALL` shows as locked, and the page has no EMRG mention. Open `/Account/Register` (logged out): the department dropdown no longer lists them. Stop the server.

- [ ] **Step 6: Commit**

```bash
cd /c/EduConnect && git add src && git commit -m "Delete the CLAS, COED and PE department tags

They have no place in the College > Department > Program hierarchy.
Also stops reserving EMRG, whose tag main already removed.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 6: Hierarchy tables and seed

**Files:**
- Create: `src/EduConnect.Web/Models/College.cs`, `Models/Department.cs`, `Models/AcademicProgram.cs`
- Modify: `src/EduConnect.Web/Data/ApplicationDbContext.cs`
- Create: `src/EduConnect.Web/Migrations/<timestamp>_AddCollegeHierarchy.cs` (+ Designer, snapshot update)

**Interfaces:**
- Produces: `DbSet<College> Colleges`, `DbSet<Department> Departments`, `DbSet<AcademicProgram> Programs`; entity properties exactly as below (Plans 2–5 use these names).

- [ ] **Step 1: Create the entities**

`Models/College.cs`:
```csharp
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EduConnect.Web.Models
{
    [Table("Colleges")]
    public class College
    {
        [Key]
        public int CollegeID { get; set; }

        [Required]
        [MaxLength(100)]
        public string Name { get; set; } = "";

        [MaxLength(20)]
        public string? ShortName { get; set; }

        // The flat DepartmentTags row this college replaces. Used to
        // backfill placement and announcement targets; NULL for colleges
        // that had no tag (College of Education & Liberal Arts).
        public int? LegacyTagID { get; set; }

        public bool IsActive { get; set; } = true;
        public DateTime? RetiredAt { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime? UpdatedAt { get; set; }

        public DepartmentTag? LegacyTag { get; set; }
        public ICollection<Department> Departments { get; set; } = new List<Department>();
    }
}
```

`Models/Department.cs`:
```csharp
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EduConnect.Web.Models
{
    [Table("Departments")]
    public class Department
    {
        [Key]
        public int DepartmentID { get; set; }

        [Required]
        public int CollegeID { get; set; }

        [Required]
        [MaxLength(150)]
        public string Name { get; set; } = "";

        [MaxLength(20)]
        public string? ShortName { get; set; }

        // Colleges without departments (Architecture, Law, Nursing,
        // Pharmacy) get exactly one implicit department so every program
        // has a department. The UI never shows implicit departments.
        public bool IsImplicit { get; set; }

        public bool IsActive { get; set; } = true;
        public DateTime? RetiredAt { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime? UpdatedAt { get; set; }

        public College College { get; set; } = null!;
        public ICollection<AcademicProgram> Programs { get; set; } = new List<AcademicProgram>();
    }
}
```

`Models/AcademicProgram.cs`:
```csharp
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EduConnect.Web.Models
{
    // Not "Program": that name is taken by the top-level-statements class
    // in Program.cs, which would win name lookup inside this project.
    [Table("Programs")]
    public class AcademicProgram
    {
        [Key]
        public int ProgramID { get; set; }

        [Required]
        public int DepartmentID { get; set; }

        [Required]
        [MaxLength(150)]
        public string Name { get; set; } = "";

        [MaxLength(20)]
        public string? ShortName { get; set; }

        public bool IsActive { get; set; } = true;
        public DateTime? RetiredAt { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime? UpdatedAt { get; set; }

        public Department Department { get; set; } = null!;
    }
}
```

- [ ] **Step 2: Register them in the DbContext**

In `ApplicationDbContext.cs`, after `public DbSet<AnnouncementTag> AnnouncementTags { get; set; }` add:
```csharp

        // ─── Academic hierarchy ────────────────────
        public DbSet<College> Colleges { get; set; }
        public DbSet<Department> Departments { get; set; }
        public DbSet<AcademicProgram> Programs { get; set; }
```
In `OnModelCreating`, after the `// ─── DepartmentTags ───` block add:
```csharp
            // ─── Academic hierarchy ────────────────
            modelBuilder.Entity<College>(entity =>
            {
                entity.HasKey(e => e.CollegeID);
                entity.HasIndex(e => e.Name).IsUnique();
                entity.HasOne(e => e.LegacyTag)
                      .WithMany()
                      .HasForeignKey(e => e.LegacyTagID)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<Department>(entity =>
            {
                entity.HasKey(e => e.DepartmentID);
                entity.HasIndex(e => new { e.CollegeID, e.Name }).IsUnique();
                entity.HasOne(e => e.College)
                      .WithMany(e => e.Departments)
                      .HasForeignKey(e => e.CollegeID)
                      .OnDelete(DeleteBehavior.Restrict);
            });

            modelBuilder.Entity<AcademicProgram>(entity =>
            {
                entity.HasKey(e => e.ProgramID);
                entity.HasIndex(e => new { e.DepartmentID, e.Name }).IsUnique();
                entity.HasOne(e => e.Department)
                      .WithMany(e => e.Programs)
                      .HasForeignKey(e => e.DepartmentID)
                      .OnDelete(DeleteBehavior.Restrict);
            });
```

- [ ] **Step 3: Scaffold the migration**

```bash
cd /c/EduConnect/src && dotnet ef migrations add AddCollegeHierarchy --project EduConnect.Web
```
Expected: `Up` creates `Colleges`, `Departments`, `Programs` with the three unique indexes and Restrict FKs. Read the generated `Up` and confirm nothing else changed (no drops/alters of existing tables). If it contains anything else, stop and report — the snapshot has drifted.

- [ ] **Step 4: Append the seed to `Up`**

At the end of the generated `Up` method, add:
```csharp
            // ─── Seed: colleges, departments, programs ───────────────
            // Keyed on names; idempotent. LegacyTagID links each college
            // to the Academic DepartmentTags row it replaces (by ShortName).
            migrationBuilder.Sql(@"
                INSERT INTO Colleges (Name, ShortName, LegacyTagID, IsActive, CreatedAt)
                SELECT v.Name, v.ShortName, t.TagID, 1, SYSDATETIME()
                FROM (VALUES
                    (N'College of Architecture',                          N'COA'),
                    (N'College of Business Administration',               N'CBA'),
                    (N'College of Computing and Information Technology',  N'CCIT'),
                    (N'College of Engineering',                           N'COE'),
                    (N'College of Law',                                   N'LAW'),
                    (N'College of Education & Liberal Arts',              N'CELA'),
                    (N'College of Nursing',                               N'CON'),
                    (N'College of Pharmacy',                              N'COP'),
                    (N'College of Science',                               N'COS')
                ) v(Name, ShortName)
                LEFT JOIN DepartmentTags t
                    ON t.ShortName = v.ShortName
                   AND t.TagTypeID = (SELECT TagTypeID FROM TagTypes WHERE TypeName = 'Academic')
                WHERE NOT EXISTS (SELECT 1 FROM Colleges c WHERE c.Name = v.Name);
            ");

            migrationBuilder.Sql(@"
                INSERT INTO Departments (CollegeID, Name, ShortName, IsImplicit, IsActive, CreatedAt)
                SELECT c.CollegeID, v.Name, v.ShortName, v.IsImplicit, 1, SYSDATETIME()
                FROM (VALUES
                    (N'College of Architecture', N'College of Architecture', NULL, 1),
                    (N'College of Law',          N'College of Law',          NULL, 1),
                    (N'College of Nursing',      N'College of Nursing',      NULL, 1),
                    (N'College of Pharmacy',     N'College of Pharmacy',     NULL, 1),

                    (N'College of Business Administration', N'Department of Accountancy',                       NULL, 0),
                    (N'College of Business Administration', N'Department of Finance and Economics',             NULL, 0),
                    (N'College of Business Administration', N'Department of Management and Marketing',          NULL, 0),
                    (N'College of Business Administration', N'Department of Customs & Supply Chain Management', NULL, 0),
                    (N'College of Business Administration', N'Department of Hospitality and Tourism',           NULL, 0),

                    (N'College of Computing and Information Technology', N'Department of Computer Science', N'CS', 0),
                    (N'College of Computing and Information Technology', N'Department of Information Technology & Information Systems', N'IT&IS', 0),

                    (N'College of Engineering', N'Department of Chemical Engineering',          NULL,  0),
                    (N'College of Engineering', N'Department of Civil Engineering',             NULL,  0),
                    (N'College of Engineering', N'Department of Computer Engineering',          NULL,  0),
                    (N'College of Engineering', N'Department of Electronics Engineering',       N'ECE', 0),
                    (N'College of Engineering', N'Department of Industrial Engineering',        NULL,  0),
                    (N'College of Engineering', N'Department of Mechanical Engineering',        NULL,  0),
                    (N'College of Engineering', N'Department of Mining Engineering and Geology', NULL, 0),

                    (N'College of Education & Liberal Arts', N'Department of Teacher Education',                   NULL, 0),
                    (N'College of Education & Liberal Arts', N'Department of Physical Education',                  NULL, 0),
                    (N'College of Education & Liberal Arts', N'Department of Political Science and Social Studies', NULL, 0),
                    (N'College of Education & Liberal Arts', N'Department of Media and Communication',             NULL, 0),

                    (N'College of Science', N'Department of Biology',    NULL, 0),
                    (N'College of Science', N'Department of Chemistry',  NULL, 0),
                    (N'College of Science', N'Department of Psychology', NULL, 0)
                ) v(CollegeName, Name, ShortName, IsImplicit)
                JOIN Colleges c ON c.Name = v.CollegeName
                WHERE NOT EXISTS (
                    SELECT 1 FROM Departments d
                    WHERE d.CollegeID = c.CollegeID AND d.Name = v.Name);
            ");

            migrationBuilder.Sql(@"
                INSERT INTO Programs (DepartmentID, Name, ShortName, IsActive, CreatedAt)
                SELECT d.DepartmentID, v.Name, v.ShortName, 1, SYSDATETIME()
                FROM (VALUES
                    (N'College of Architecture', N'College of Architecture', N'Bachelor of Science in Architecture', N'BSArch'),
                    (N'College of Law',          N'College of Law',          N'Juris Doctor',                        N'JD'),
                    (N'College of Nursing',      N'College of Nursing',      N'BS in Nursing',                       N'BSN'),
                    (N'College of Pharmacy',     N'College of Pharmacy',     N'BS in Pharmacy',                      N'BSPharm'),

                    (N'College of Business Administration', N'Department of Accountancy',                       N'BS in Accountancy',                                           N'BSA'),
                    (N'College of Business Administration', N'Department of Finance and Economics',             N'BS in Business Administration Major in Financial Management', N'BSBA-FM'),
                    (N'College of Business Administration', N'Department of Management and Marketing',          N'BSBA Major in Marketing Management',                          N'BSBA-MM'),
                    (N'College of Business Administration', N'Department of Management and Marketing',          N'BSBA Major in Operations Management',                         N'BSBA-OM'),
                    (N'College of Business Administration', N'Department of Customs & Supply Chain Management', N'BS in Customs Administration',                                N'BSCA'),
                    (N'College of Business Administration', N'Department of Customs & Supply Chain Management', N'BS in Supply Chain Management',                               N'BSSCM'),
                    (N'College of Business Administration', N'Department of Hospitality and Tourism',           N'BS in Hospitality Management',                                N'BSHM'),

                    (N'College of Computing and Information Technology', N'Department of Computer Science', N'BS in Computer Science',                              N'BSCS'),
                    (N'College of Computing and Information Technology', N'Department of Computer Science', N'BS in Computer Science and Information Engineering', N'BSCSIE'),
                    (N'College of Computing and Information Technology', N'Department of Information Technology & Information Systems', N'BS in Information Systems',   N'BSIS'),
                    (N'College of Computing and Information Technology', N'Department of Information Technology & Information Systems', N'BS in Information Technology', N'BSIT'),

                    (N'College of Engineering', N'Department of Chemical Engineering',           N'BS in Chemical Engineering',     N'BSChE'),
                    (N'College of Engineering', N'Department of Chemical Engineering',           N'BS in Chemical Process Technology', N'BSCPT'),
                    (N'College of Engineering', N'Department of Civil Engineering',              N'BS in Civil Engineering',        N'BSCE'),
                    (N'College of Engineering', N'Department of Computer Engineering',           N'BS in Computer Engineering',     N'BSCpE'),
                    (N'College of Engineering', N'Department of Electronics Engineering',        N'BS in Electrical Engineering',   N'BSEE'),
                    (N'College of Engineering', N'Department of Electronics Engineering',        N'BS in Electronics Engineering',  N'BSECE'),
                    (N'College of Engineering', N'Department of Industrial Engineering',         N'BS in Industrial Engineering',   N'BSIE'),
                    (N'College of Engineering', N'Department of Mechanical Engineering',         N'BS in Mechanical Engineering',   N'BSME'),
                    (N'College of Engineering', N'Department of Mining Engineering and Geology', N'BS in Mining Engineering',       N'BSEM'),
                    (N'College of Engineering', N'Department of Mining Engineering and Geology', N'BS in Geology',                  N'BSGeo'),
                    (N'College of Engineering', N'Department of Mining Engineering and Geology', N'BS in Petroleum Engineering',    N'BSPetE'),

                    (N'College of Education & Liberal Arts', N'Department of Teacher Education',                    N'Bachelor of Elementary Education',                                  N'BEEd'),
                    (N'College of Education & Liberal Arts', N'Department of Teacher Education',                    N'Bachelor of Secondary Education Major in English',                  N'BSEd-Eng'),
                    (N'College of Education & Liberal Arts', N'Department of Physical Education',                   N'Bachelor of Physical Education',                                    N'BPEd'),
                    (N'College of Education & Liberal Arts', N'Department of Physical Education',                   N'Bachelor of Physical Education Major in Sports and Wellness Management', N'BPEd-SWM'),
                    (N'College of Education & Liberal Arts', N'Department of Political Science and Social Studies', N'BA in Political Science',                                           N'ABPolSci'),
                    (N'College of Education & Liberal Arts', N'Department of Political Science and Social Studies', N'BA in Philosophy',                                                  N'ABPhilo'),
                    (N'College of Education & Liberal Arts', N'Department of Media and Communication',              N'BA in Communication',                                               N'ABComm'),

                    (N'College of Science', N'Department of Biology',    N'BS in Biology',    N'BSBio'),
                    (N'College of Science', N'Department of Chemistry',  N'BS in Chemistry',  N'BSChem'),
                    (N'College of Science', N'Department of Psychology', N'BS in Psychology', N'BSPsych')
                ) v(CollegeName, DepartmentName, Name, ShortName)
                JOIN Colleges c    ON c.Name = v.CollegeName
                JOIN Departments d ON d.CollegeID = c.CollegeID AND d.Name = v.DepartmentName
                WHERE NOT EXISTS (
                    SELECT 1 FROM Programs p
                    WHERE p.DepartmentID = d.DepartmentID AND p.Name = v.Name);
            ");
```
The generated `Down` (drop the three tables) stays as scaffolded — dropping the tables removes the seed.

- [ ] **Step 5: Build and apply**

```bash
cd /c/EduConnect/src && dotnet build EduConnect.Web 2>&1 | grep -E " error |Build succeeded" && dotnet ef database update --project EduConnect.Web
```
Expected: build succeeds, migration applies.

- [ ] **Step 6: Assert the seed**

```bash
sqlcmd -S 'localhost\SQLEXPRESS' -d EduConnectDB -E -C -W -Q "SET NOCOUNT ON; SELECT (SELECT COUNT(*) FROM Colleges) colleges, (SELECT COUNT(*) FROM Departments) depts, (SELECT COUNT(*) FROM Departments WHERE IsImplicit=1) implicitDepts, (SELECT COUNT(*) FROM Programs) programs, (SELECT COUNT(*) FROM Colleges WHERE LegacyTagID IS NULL) noLegacy; SELECT c.ShortName, COUNT(DISTINCT d.DepartmentID) d, COUNT(p.ProgramID) p FROM Colleges c JOIN Departments d ON d.CollegeID=c.CollegeID LEFT JOIN Programs p ON p.DepartmentID=d.DepartmentID GROUP BY c.ShortName ORDER BY c.ShortName"
```
Expected: `colleges 9, depts 25, implicitDepts 4, programs 36, noLegacy 1` (CELA). Per college: CBA 5/7, CCIT 2/4, CELA 4/7, COA 1/1, COE 7/11, CON 1/1, COP 1/1, COS 3/3, LAW 1/1.

- [ ] **Step 7: Prove idempotency (Review Focus 3)**

Copy the three `INSERT` statements from Step 4 into a scratch file `seed.sql` in the session scratchpad and run it again:
```bash
sqlcmd -S 'localhost\SQLEXPRESS' -d EduConnectDB -E -C -b -i "<scratchpad>/seed.sql" && sqlcmd -S 'localhost\SQLEXPRESS' -d EduConnectDB -E -C -W -h -1 -Q "SET NOCOUNT ON; SELECT COUNT(*) FROM Colleges; SELECT COUNT(*) FROM Departments; SELECT COUNT(*) FROM Programs"
```
Expected: no error; counts still 9, 25, 36.

- [ ] **Step 8: Commit**

```bash
cd /c/EduConnect && git add src && git commit -m "Add the College > Department > Program hierarchy

Seeds the nine colleges, their departments and 36 programs. Colleges
without departments get one implicit department the UI never shows.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 7: User placement columns and college backfill

**Files:**
- Modify: `src/EduConnect.Web/Models/User.cs`, `src/EduConnect.Web/Data/ApplicationDbContext.cs`
- Create: `src/EduConnect.Web/Migrations/<timestamp>_AddUserPlacement.cs` (+ Designer, snapshot)
- Modify: `src/CLAUDE.md`

**Interfaces:**
- Consumes: `College`, `Department`, `AcademicProgram` (Task 6).
- Produces: `User.CollegeID`, `User.DepartmentID`, `User.ProgramID` (all `int?`) and navigations `User.College`, `User.Department`, `User.AcademicProgram`.

- [ ] **Step 1: Add the properties**

In `Models/User.cs`, after `public string? Suffix { get; set; }` add:
```csharp

        // Placement in the academic hierarchy. Stored at every level so
        // feed and routing queries filter on one column; Plan 2's
        // placement service is the only writer and keeps them consistent.
        // Dean: College. Chairperson/Faculty: Department (+College).
        // Student: Program (+Department, +College). Others: none.
        public int? CollegeID { get; set; }
        public int? DepartmentID { get; set; }
        public int? ProgramID { get; set; }
```
and after `public User? VerifiedBy { get; set; }` add:
```csharp
        public College? College { get; set; }
        public Department? Department { get; set; }
        public AcademicProgram? AcademicProgram { get; set; }
```

- [ ] **Step 2: Configure the FKs**

In `ApplicationDbContext.OnModelCreating`, inside the existing `// User verifiedby relationship` block (`modelBuilder.Entity<User>(entity => { ... })`), after the `VerifiedBy` configuration add:
```csharp
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
```

- [ ] **Step 3: Scaffold the migration**

```bash
cd /c/EduConnect/src && dotnet ef migrations add AddUserPlacement --project EduConnect.Web
```
Expected `Up`: three nullable int columns on `Users`, three indexes, three FKs with `ReferentialAction.Restrict`. Nothing else — if there is, stop and report.

- [ ] **Step 4: Append the backfill to `Up`**

At the end of `Up`:
```csharp
            // Existing academic users were tagged at what is really college
            // level. Carry that over; department/program placement is
            // manual (admin for staff, the student for themself).
            // Users whose primary tag is not a college (ALL, offices) or who
            // have no tag keep NULL.
            migrationBuilder.Sql(@"
                UPDATE u
                SET CollegeID = c.CollegeID
                FROM Users u
                JOIN UserDepartments ud ON ud.UserID = u.UserID AND ud.IsPrimary = 1
                JOIN Colleges c ON c.LegacyTagID = ud.TagID
                WHERE u.CollegeID IS NULL;
            ");
```

- [ ] **Step 5: Build, apply, assert (Review Focus 4)**

```bash
cd /c/EduConnect/src && dotnet build EduConnect.Web 2>&1 | grep -E " error |Build succeeded" && dotnet ef database update --project EduConnect.Web
sqlcmd -S 'localhost\SQLEXPRESS' -d EduConnectDB -E -C -W -Q "SET NOCOUNT ON; SELECT c.ShortName, COUNT(*) n FROM Users u JOIN Colleges c ON c.CollegeID=u.CollegeID GROUP BY c.ShortName ORDER BY 1; SELECT r.RoleName, COUNT(*) unplaced FROM Users u JOIN Roles r ON r.RoleID=u.RoleID WHERE u.CollegeID IS NULL GROUP BY r.RoleName; SELECT COUNT(*) mismatched FROM Users u JOIN UserDepartments ud ON ud.UserID=u.UserID AND ud.IsPrimary=1 JOIN Colleges c ON c.LegacyTagID=ud.TagID WHERE u.CollegeID <> c.CollegeID"
```
Expected locally: `CCIT 16, COA 1, COE 1, COS 4`; unplaced exactly `Administrator 1`, `Faculty 1` (the ex-adviser tagged `ALL`), `Staff 2`; `mismatched 0`. Every unplaced user has a non-college or missing primary tag.

- [ ] **Step 6: Smoke test**

Start the server; log in as admin and as `uitest.faculty@educonnect.edu`; open the dashboard, `/Announcement`, `/Admin/Users` (admin) to confirm nothing that loads `User` broke. Stop the server.

- [ ] **Step 7: Update CLAUDE.md**

In `src/CLAUDE.md` under "Key Domain Concepts", after the **DepartmentTags** paragraph add:
```markdown
**Academic hierarchy** (`Colleges` > `Departments` > `Programs`, entity `AcademicProgram`) is replacing the academic rows of `DepartmentTags`. Colleges without departments have one `IsImplicit` department that the UI hides. `Users.CollegeID/DepartmentID/ProgramID` hold a user's placement (Dean: college; Chairperson/Faculty: department; Student: program). `DepartmentTags` stays for `ALL` and non-academic offices. Design: `docs/superpowers/specs/2026-09-28-college-hierarchy-and-roles-design.md`.
```

- [ ] **Step 8: Commit**

```bash
cd /c/EduConnect && git add src && git commit -m "Give users a place in the academic hierarchy

Adds college, department and program placement to Users and carries
each academic user's old college tag over to their college.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

## After this plan

Report to the user: the five new migrations to apply on Azure (in order), the Task 5 pre-check query to run there first, and that Chairpersons and ex-advisers will be asked to log in again. Then write Plan 2 (Admin hierarchy CRUD + user placement + test project) against the code as merged.
