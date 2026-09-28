# Admin Hierarchy & Placement Implementation Plan (Plan 2 of 5)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let the System Administrator manage the College > Department > Program hierarchy (add, rename, retire, restore) and place any user — Dean, Chairperson, Faculty or Student — in it, with an automated test project guarding the rules.

**Architecture:** All hierarchy and placement rules live in two services, `HierarchyService` and `PlacementService`, so the controllers stay thin and the rules are unit-tested against an in-memory SQLite copy of the real EF model. A new `AcademicStructureController` renders the hierarchy as one tree page. `AdminController`'s Add/Edit User forms gain a role-aware College → Department → Program picker (shared partial) and the Users list gains a placement column and a "Needs placement" filter.

**Tech Stack:** ASP.NET Core 8 MVC, EF Core 8 (SQL Server; SQLite in-memory for tests), xUnit, Razor, Bootstrap 5.1, vanilla JS.

**Spec:** `docs/superpowers/specs/2026-09-28-college-hierarchy-and-roles-design.md`

## Global Constraints

- Retire = `IsActive = false` + `RetiredAt = DateTime.Now`; restore clears `RetiredAt`. Never hard-delete hierarchy rows.
- An item cannot be retired while any user (active, pending or deactivated) is placed in it, or while it has active children. Restore is refused while its parent is retired.
- Implicit departments are never shown, renamed, or retired on their own; they follow their college.
- Names are trimmed, required, unique within their parent case-insensitively; college name ≤ 100, department/program name ≤ 150, short code ≤ 20.
- Placement per role (spec table): Dean → College; Chairperson/Faculty → Department (+College); Student/Student Pending → Program (+Department, +College); every other role → none. The most specific ID posted wins; less specific IDs are derived, never trusted.
- A Chairperson cannot be placed in an implicit department (those colleges have no Chairperson).
- `DepartmentTags`/`UserDepartments` stay as they are in this plan (the feed still reads them until Plan 4); the user form keeps its tag field, relabelled "Feed tag".
- Role comparisons use `RoleNames.*` — never a literal.
- Bootstrap is 5.1: no `--bs-*` component variables, no `text-bg-*`.
- Stop the dev server before `dotnet build`/`dotnet test` of the web project (exe lock).
- Never push `main`. Commit messages: sentence-case imperative, no prefix, ending `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- Test command for every task: `cd /c/EduConnect/src && dotnet test EduConnect.Tests` (plus `dotnet build EduConnect.Web` for view changes).

## Review Focus

1. **A tampered or stale user form** — e.g. a program from CCIT posted with the College of Science's ID, or a Dean form still carrying a department/program from before the role changed. Expected: the most specific level wins for roles that use it, and roles that don't use a level ignore it. Tests: `Student_ProgramWinsOverMismatchedCollege`, `Dean_IgnoresPostedDepartmentAndProgram` (Task 4).
2. **Retiring a program whose only placed user is a pending (inactive) student** — expected: refused, because approval would otherwise put a student in a retired program. Test: `RetireProgram_WithInactiveUserPlaced_Fails` (Task 2).
3. **Duplicate names that differ only by case or surrounding spaces** — expected: refused with a clear message. Test: `AddCollege_DuplicateNameDifferentCaseAndSpaces_Fails` (Task 1).
4. **Restoring a child under a retired parent** — expected: refused ("restore the college first"). Test: `RestoreDepartment_UnderRetiredCollege_Fails` (Task 2).
5. **Changing a placed user to Administrator/Staff** — expected: all three placement columns cleared, not left stale for Plan 4's audience resolver. Test: `Staff_ClearsExistingPlacement` (Task 4).

---

## File Structure

| File | Responsibility |
|---|---|
| `src/EduConnect.Tests/EduConnect.Tests.csproj` (create) | xUnit test project |
| `src/EduConnect.Tests/TestDb.cs` (create) | In-memory SQLite `ApplicationDbContext` + seed helpers |
| `src/EduConnect.Tests/HierarchyServiceTests.cs` (create) | Hierarchy rules |
| `src/EduConnect.Tests/PlacementServiceTests.cs` (create) | Placement rules |
| `src/EduConnect.Web/Services/IHierarchyService.cs` (create) | Interface + `HierarchyResult` |
| `src/EduConnect.Web/Services/HierarchyService.cs` (create) | Tree read, add, rename, retire/restore |
| `src/EduConnect.Web/Services/IPlacementService.cs` (create) | Interface |
| `src/EduConnect.Web/Services/PlacementService.cs` (create) | Apply placement per role; `NeedsPlacement` predicate |
| `src/EduConnect.Web/Controllers/AcademicStructureController.cs` (create) | Admin hierarchy page actions |
| `src/EduConnect.Web/Views/AcademicStructure/Index.cshtml` (create) | Hierarchy tree page |
| `src/EduConnect.Web/Views/Admin/_PlacementFields.cshtml` (create) | Role-aware placement picker partial |
| `src/EduConnect.Web/ViewModel/AdminUserFormViewModel.cs` (modify) | Placement fields + tree |
| `src/EduConnect.Web/Controllers/AdminController.cs` (modify) | Inject services; user forms; Users filter |
| `src/EduConnect.Web/Views/Admin/{AddUser,EditUser,Users,Departments}.cshtml` (modify) | Picker, column, filter, relabel |
| `src/EduConnect.Web/Views/Shared/_SidebarContent.cshtml` (modify) | Nav links |
| `src/EduConnect.Web/Program.cs` (modify) | DI registrations |
| `src/EduConnect.Web.slnx`, `src/CLAUDE.md` (modify) | Add test project; test command |

---

### Task 1: Test project and hierarchy reads/adds

**Files:**
- Create: `src/EduConnect.Tests/EduConnect.Tests.csproj`, `src/EduConnect.Tests/TestDb.cs`, `src/EduConnect.Tests/HierarchyServiceTests.cs`
- Create: `src/EduConnect.Web/Services/IHierarchyService.cs`, `src/EduConnect.Web/Services/HierarchyService.cs`
- Modify: `src/EduConnect.Web.slnx`, `src/EduConnect.Web/Program.cs`, `src/CLAUDE.md`

**Interfaces:**
- Produces:
  - `record HierarchyResult(bool Ok, string? Error)` with `static HierarchyResult Success` and `static HierarchyResult Fail(string error)` (namespace `EduConnect.Web.Services`)
  - `IHierarchyService.GetTreeAsync(bool includeRetired) : Task<List<College>>` — colleges ordered by name, each with `Departments` (ordered by name, implicit included) and their `Programs` (ordered by name); with `includeRetired == false`, retired colleges/departments/programs are removed.
  - `AddCollegeAsync(string? name, string? shortName, bool hasDepartments) : Task<HierarchyResult>`
  - `AddDepartmentAsync(int collegeId, string? name, string? shortName) : Task<HierarchyResult>`
  - `AddProgramAsync(int departmentId, string? name, string? shortName) : Task<HierarchyResult>`
  - Test helper `TestDb` with `Context`, `NewContext()`, `AddCollege(string name, bool flat = false)`, `AddDepartment(College c, string name)`, `AddProgram(Department d, string name)`, `AddUser(string roleName, int? collegeId = null, int? departmentId = null, int? programId = null, bool isActive = true)`.

- [ ] **Step 1: Create the test project**

```bash
cd /c/EduConnect/src && dotnet new xunit -o EduConnect.Tests --framework net8.0 && rm -f EduConnect.Tests/UnitTest1.cs
cd EduConnect.Tests && dotnet add package Microsoft.EntityFrameworkCore.Sqlite --version 8.0.0 && dotnet add reference ../EduConnect.Web/EduConnect.Web.csproj
cd .. && dotnet sln EduConnect.Web.slnx add EduConnect.Tests/EduConnect.Tests.csproj
```
Expected: project created, package and reference added, `EduConnect.Web.slnx` lists both projects. If `dotnet sln` rejects the `.slnx`, add `<Project Path="EduConnect.Tests/EduConnect.Tests.csproj" />` inside `<Solution>` by hand.

- [ ] **Step 2: Write the test database helper**

`src/EduConnect.Tests/TestDb.cs`:
```csharp
using EduConnect.Web.Data;
using EduConnect.Web.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace EduConnect.Tests
{
    // One in-memory SQLite database per test: the real EF model with real
    // unique indexes and foreign keys, without needing SQL Server.
    public sealed class TestDb : IDisposable
    {
        private readonly SqliteConnection _connection;

        public ApplicationDbContext Context { get; }

        public TestDb()
        {
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();
            Context = NewContext();
            Context.Database.EnsureCreated();
        }

        // A second context on the same database, for asserting what was
        // actually saved rather than what the first context has cached.
        public ApplicationDbContext NewContext() =>
            new(new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlite(_connection)
                .Options);

        public College AddCollege(string name, bool flat = false)
        {
            var college = new College { Name = name };
            if (flat)
                college.Departments.Add(new Department { Name = name, IsImplicit = true });
            Context.Colleges.Add(college);
            Context.SaveChanges();
            return college;
        }

        public Department AddDepartment(College college, string name)
        {
            var dept = new Department { CollegeID = college.CollegeID, Name = name };
            Context.Departments.Add(dept);
            Context.SaveChanges();
            return dept;
        }

        public AcademicProgram AddProgram(Department dept, string name)
        {
            var program = new AcademicProgram { DepartmentID = dept.DepartmentID, Name = name };
            Context.Programs.Add(program);
            Context.SaveChanges();
            return program;
        }

        public User AddUser(string roleName, int? collegeId = null,
            int? departmentId = null, int? programId = null, bool isActive = true)
        {
            var role = Context.Roles.FirstOrDefault(r => r.RoleName == roleName);
            if (role == null)
            {
                role = new Role { RoleName = roleName, RoleLevel = 1 };
                Context.Roles.Add(role);
                Context.SaveChanges();
            }

            var user = new User
            {
                FirstName = "Test",
                LastName = roleName,
                Email = $"{Guid.NewGuid():N}@test.local",
                PasswordHash = "x",
                RoleID = role.RoleID,
                IsActive = isActive,
                CollegeID = collegeId,
                DepartmentID = departmentId,
                ProgramID = programId
            };
            Context.Users.Add(user);
            Context.SaveChanges();
            return user;
        }

        public void Dispose()
        {
            Context.Dispose();
            _connection.Dispose();
        }
    }
}
```

- [ ] **Step 3: Write the failing tests**

`src/EduConnect.Tests/HierarchyServiceTests.cs`:
```csharp
using EduConnect.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace EduConnect.Tests
{
    public class HierarchyServiceTests : IDisposable
    {
        private readonly TestDb _db = new();
        private HierarchyService Service => new(_db.Context);

        public void Dispose() => _db.Dispose();

        [Fact]
        public async Task AddCollege_WithDepartments_CreatesNoImplicitDepartment()
        {
            var result = await Service.AddCollegeAsync("College of Science", "COS", hasDepartments: true);

            Assert.True(result.Ok);
            var college = await _db.NewContext().Colleges.Include(c => c.Departments).SingleAsync();
            Assert.Equal("College of Science", college.Name);
            Assert.Equal("COS", college.ShortName);
            Assert.Empty(college.Departments);
        }

        [Fact]
        public async Task AddCollege_WithoutDepartments_CreatesOneImplicitDepartment()
        {
            var result = await Service.AddCollegeAsync("College of Law", "LAW", hasDepartments: false);

            Assert.True(result.Ok);
            var dept = await _db.NewContext().Departments.SingleAsync();
            Assert.True(dept.IsImplicit);
            Assert.Equal("College of Law", dept.Name);
        }

        [Fact]
        public async Task AddCollege_DuplicateNameDifferentCaseAndSpaces_Fails()
        {
            _db.AddCollege("College of Law");

            var result = await Service.AddCollegeAsync("  college of LAW ", null, hasDepartments: false);

            Assert.False(result.Ok);
            Assert.Contains("already exists", result.Error);
            Assert.Equal(1, await _db.NewContext().Colleges.CountAsync());
        }

        [Fact]
        public async Task AddCollege_BlankName_Fails()
        {
            var result = await Service.AddCollegeAsync("   ", null, hasDepartments: true);

            Assert.False(result.Ok);
            Assert.Contains("required", result.Error);
        }

        [Fact]
        public async Task AddCollege_ShortCodeTooLong_Fails()
        {
            var result = await Service.AddCollegeAsync("College of X", new string('A', 21), hasDepartments: true);

            Assert.False(result.Ok);
            Assert.Contains("20", result.Error);
        }

        [Fact]
        public async Task AddDepartment_ToStructuredCollege_Succeeds()
        {
            var college = _db.AddCollege("CCIT");

            var result = await Service.AddDepartmentAsync(college.CollegeID, "Department of Computer Science", "CS");

            Assert.True(result.Ok);
            var dept = await _db.NewContext().Departments.SingleAsync();
            Assert.False(dept.IsImplicit);
            Assert.Equal(college.CollegeID, dept.CollegeID);
        }

        [Fact]
        public async Task AddDepartment_ToFlatCollege_Fails()
        {
            var college = _db.AddCollege("College of Nursing", flat: true);

            var result = await Service.AddDepartmentAsync(college.CollegeID, "Department of X", null);

            Assert.False(result.Ok);
            Assert.Contains("no departments", result.Error);
        }

        [Fact]
        public async Task AddDepartment_ToRetiredCollege_Fails()
        {
            var college = _db.AddCollege("CCIT");
            college.IsActive = false;
            _db.Context.SaveChanges();

            var result = await Service.AddDepartmentAsync(college.CollegeID, "Department of Computer Science", null);

            Assert.False(result.Ok);
            Assert.Contains("retired", result.Error);
        }

        [Fact]
        public async Task AddDepartment_DuplicateInSameCollege_Fails()
        {
            var college = _db.AddCollege("CCIT");
            _db.AddDepartment(college, "Department of Computer Science");

            var result = await Service.AddDepartmentAsync(college.CollegeID, "department of computer science", null);

            Assert.False(result.Ok);
            Assert.Contains("already exists", result.Error);
        }

        [Fact]
        public async Task AddProgram_ToDepartment_Succeeds()
        {
            var dept = _db.AddDepartment(_db.AddCollege("CCIT"), "IT&IS");

            var result = await Service.AddProgramAsync(dept.DepartmentID, "BS in Information Technology", "BSIT");

            Assert.True(result.Ok);
            var program = await _db.NewContext().Programs.SingleAsync();
            Assert.Equal(dept.DepartmentID, program.DepartmentID);
            Assert.Equal("BSIT", program.ShortName);
        }

        [Fact]
        public async Task AddProgram_ToRetiredDepartment_Fails()
        {
            var dept = _db.AddDepartment(_db.AddCollege("CCIT"), "IT&IS");
            dept.IsActive = false;
            _db.Context.SaveChanges();

            var result = await Service.AddProgramAsync(dept.DepartmentID, "BS in Information Technology", null);

            Assert.False(result.Ok);
            Assert.Contains("retired", result.Error);
        }

        [Fact]
        public async Task GetTree_WithoutRetired_DropsRetiredItemsAtEveryLevel()
        {
            var ccit = _db.AddCollege("CCIT");
            var cs = _db.AddDepartment(ccit, "CS");
            var itis = _db.AddDepartment(ccit, "IT&IS");
            _db.AddProgram(cs, "BSCS");
            var retiredProgram = _db.AddProgram(cs, "BSOld");
            var retiredCollege = _db.AddCollege("Old College");
            itis.IsActive = false;
            retiredProgram.IsActive = false;
            retiredCollege.IsActive = false;
            _db.Context.SaveChanges();

            var tree = await Service.GetTreeAsync(includeRetired: false);

            var college = Assert.Single(tree);
            var dept = Assert.Single(college.Departments);
            Assert.Equal("CS", dept.Name);
            Assert.Equal("BSCS", Assert.Single(dept.Programs).Name);
        }

        [Fact]
        public async Task GetTree_WithRetired_KeepsEverythingOrderedByName()
        {
            var b = _db.AddCollege("B College");
            _db.AddCollege("A College");
            b.IsActive = false;
            _db.Context.SaveChanges();

            var tree = await Service.GetTreeAsync(includeRetired: true);

            Assert.Equal(new[] { "A College", "B College" }, tree.Select(c => c.Name));
        }
    }
}
```

- [ ] **Step 4: Run the tests to verify they fail**

Run: `cd /c/EduConnect/src && dotnet test EduConnect.Tests 2>&1 | tail -5`
Expected: build FAILS with `The type or namespace name 'HierarchyService' could not be found`.

- [ ] **Step 5: Write the interface**

`src/EduConnect.Web/Services/IHierarchyService.cs`:
```csharp
using EduConnect.Web.Models;

namespace EduConnect.Web.Services
{
    public record HierarchyResult(bool Ok, string? Error)
    {
        public static HierarchyResult Success { get; } = new(true, null);
        public static HierarchyResult Fail(string error) => new(false, error);
    }

    // Every rule about adding, renaming, retiring and restoring colleges,
    // departments and programs. Controllers call this and show Error.
    public interface IHierarchyService
    {
        Task<List<College>> GetTreeAsync(bool includeRetired);

        Task<HierarchyResult> AddCollegeAsync(string? name, string? shortName, bool hasDepartments);
        Task<HierarchyResult> AddDepartmentAsync(int collegeId, string? name, string? shortName);
        Task<HierarchyResult> AddProgramAsync(int departmentId, string? name, string? shortName);
    }
}
```

- [ ] **Step 6: Write the implementation**

`src/EduConnect.Web/Services/HierarchyService.cs`:
```csharp
using EduConnect.Web.Data;
using EduConnect.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace EduConnect.Web.Services
{
    public class HierarchyService : IHierarchyService
    {
        private const int CollegeNameMax = 100;
        private const int NameMax = 150;
        private const int ShortNameMax = 20;

        private readonly ApplicationDbContext _context;

        public HierarchyService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<List<College>> GetTreeAsync(bool includeRetired)
        {
            // Small table (~70 rows): load it whole, then shape in memory.
            var colleges = await _context.Colleges
                .Include(c => c.Departments)
                    .ThenInclude(d => d.Programs)
                .AsNoTracking()
                .ToListAsync();

            if (!includeRetired)
                colleges = colleges.Where(c => c.IsActive).ToList();

            foreach (var college in colleges)
            {
                college.Departments = college.Departments
                    .Where(d => includeRetired || d.IsActive)
                    .OrderBy(d => d.Name)
                    .ToList();

                foreach (var dept in college.Departments)
                    dept.Programs = dept.Programs
                        .Where(p => includeRetired || p.IsActive)
                        .OrderBy(p => p.Name)
                        .ToList();
            }

            return colleges.OrderBy(c => c.Name).ToList();
        }

        public async Task<HierarchyResult> AddCollegeAsync(
            string? name, string? shortName, bool hasDepartments)
        {
            name = Clean(name);
            shortName = Clean(shortName);
            var invalid = ValidateNames(name, CollegeNameMax, shortName);
            if (invalid != null)
                return invalid;

            var lower = name!.ToLower();
            if (await _context.Colleges.AnyAsync(c => c.Name.ToLower() == lower))
                return HierarchyResult.Fail($"A college named \"{name}\" already exists.");

            var college = new College { Name = name, ShortName = shortName };

            // A college without departments still needs one to hang its
            // programs on; the UI never shows it.
            if (!hasDepartments)
                college.Departments.Add(new Department { Name = name, IsImplicit = true });

            _context.Colleges.Add(college);
            await _context.SaveChangesAsync();
            return HierarchyResult.Success;
        }

        public async Task<HierarchyResult> AddDepartmentAsync(
            int collegeId, string? name, string? shortName)
        {
            name = Clean(name);
            shortName = Clean(shortName);
            var invalid = ValidateNames(name, NameMax, shortName);
            if (invalid != null)
                return invalid;

            var college = await _context.Colleges
                .Include(c => c.Departments)
                .FirstOrDefaultAsync(c => c.CollegeID == collegeId);

            if (college == null)
                return HierarchyResult.Fail("College not found.");
            if (!college.IsActive)
                return HierarchyResult.Fail($"{college.Name} is retired. Restore it first.");
            if (college.Departments.Any(d => d.IsImplicit))
                return HierarchyResult.Fail(
                    $"{college.Name} has no departments; add programs to it directly.");

            var lower = name!.ToLower();
            if (college.Departments.Any(d => d.Name.ToLower() == lower))
                return HierarchyResult.Fail(
                    $"{college.Name} already has a department named \"{name}\".");

            college.Departments.Add(new Department { Name = name, ShortName = shortName });
            await _context.SaveChangesAsync();
            return HierarchyResult.Success;
        }

        public async Task<HierarchyResult> AddProgramAsync(
            int departmentId, string? name, string? shortName)
        {
            name = Clean(name);
            shortName = Clean(shortName);
            var invalid = ValidateNames(name, NameMax, shortName);
            if (invalid != null)
                return invalid;

            var dept = await _context.Departments
                .Include(d => d.College)
                .Include(d => d.Programs)
                .FirstOrDefaultAsync(d => d.DepartmentID == departmentId);

            if (dept == null)
                return HierarchyResult.Fail("Department not found.");
            if (!dept.College.IsActive)
                return HierarchyResult.Fail($"{dept.College.Name} is retired. Restore it first.");
            if (!dept.IsActive)
                return HierarchyResult.Fail($"{dept.Name} is retired. Restore it first.");

            var lower = name!.ToLower();
            if (dept.Programs.Any(p => p.Name.ToLower() == lower))
                return HierarchyResult.Fail(
                    $"{DisplayName(dept)} already has a program named \"{name}\".");

            dept.Programs.Add(new AcademicProgram { Name = name, ShortName = shortName });
            await _context.SaveChangesAsync();
            return HierarchyResult.Success;
        }

        // ─── Helpers ───────────────────────────────

        private static string? Clean(string? value) =>
            string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        private static HierarchyResult? ValidateNames(string? name, int nameMax, string? shortName)
        {
            if (name == null)
                return HierarchyResult.Fail("Name is required.");
            if (name.Length > nameMax)
                return HierarchyResult.Fail($"Name must be {nameMax} characters or fewer.");
            if (shortName != null && shortName.Length > ShortNameMax)
                return HierarchyResult.Fail($"Short code must be {ShortNameMax} characters or fewer.");
            return null;
        }

        // An implicit department is shown as its college.
        private static string DisplayName(Department dept) =>
            dept.IsImplicit ? dept.College.Name : dept.Name;
    }
}
```

- [ ] **Step 7: Run the tests to verify they pass**

Run: `cd /c/EduConnect/src && dotnet test EduConnect.Tests 2>&1 | tail -3`
Expected: `Passed!  - Failed: 0, Passed: 13`.

- [ ] **Step 8: Register the service and document the test command**

In `src/EduConnect.Web/Program.cs`, after the `IBlobStorageService` registration line add:
```csharp
builder.Services.AddScoped<EduConnect.Web.Services.IHierarchyService, EduConnect.Web.Services.HierarchyService>();
```
In `src/CLAUDE.md` replace the line `There are no automated tests in this project.` with:
```markdown
# Run the tests (xUnit, in-memory SQLite — no SQL Server needed)
dotnet test EduConnect.Tests
```
placed inside the Commands code block (before its closing fence), and delete the old sentence.

Run: `cd /c/EduConnect/src && dotnet build EduConnect.Web 2>&1 | grep -E " error |Build succeeded" | sort -u && dotnet test EduConnect.Tests 2>&1 | tail -1`
Expected: `Build succeeded.` and `Passed!  - Failed: 0, Passed: 13`.

- [ ] **Step 9: Commit**

```bash
cd /c/EduConnect && git add src && git commit -m "Add a test project and the hierarchy service's add rules

HierarchyService owns adding colleges, departments and programs; the
new xUnit project checks its rules against an in-memory copy of the
real EF model.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 2: Rename, retire and restore rules

**Files:**
- Modify: `src/EduConnect.Web/Services/IHierarchyService.cs`, `src/EduConnect.Web/Services/HierarchyService.cs`
- Test: `src/EduConnect.Tests/HierarchyServiceTests.cs`

**Interfaces:**
- Consumes: `HierarchyResult`, `HierarchyService` helpers `Clean`, `ValidateNames`, `DisplayName` (Task 1).
- Produces on `IHierarchyService`:
  - `RenameCollegeAsync(int id, string? name, string? shortName)`, `RenameDepartmentAsync(int id, string? name, string? shortName)`, `RenameProgramAsync(int id, string? name, string? shortName)` — all `Task<HierarchyResult>`
  - `SetCollegeActiveAsync(int id, bool active)`, `SetDepartmentActiveAsync(int id, bool active)`, `SetProgramActiveAsync(int id, bool active)` — all `Task<HierarchyResult>`

- [ ] **Step 1: Write the failing tests**

Append inside `HierarchyServiceTests` (before the final closing braces):
```csharp
        [Fact]
        public async Task RenameCollege_Flat_AlsoRenamesItsImplicitDepartment()
        {
            var college = _db.AddCollege("College of Law", flat: true);

            var result = await Service.RenameCollegeAsync(college.CollegeID, "School of Law", "SOL");

            Assert.True(result.Ok);
            var ctx = _db.NewContext();
            Assert.Equal("School of Law", (await ctx.Colleges.SingleAsync()).Name);
            Assert.Equal("School of Law", (await ctx.Departments.SingleAsync()).Name);
        }

        [Fact]
        public async Task RenameCollege_ToAnotherCollegesName_Fails()
        {
            _db.AddCollege("College of Law");
            var other = _db.AddCollege("College of Nursing");

            var result = await Service.RenameCollegeAsync(other.CollegeID, "college of law", null);

            Assert.False(result.Ok);
            Assert.Contains("already exists", result.Error);
        }

        [Fact]
        public async Task RenameCollege_SameNameDifferentCase_Succeeds()
        {
            var college = _db.AddCollege("College of law");

            var result = await Service.RenameCollegeAsync(college.CollegeID, "College of Law", "LAW");

            Assert.True(result.Ok);
        }

        [Fact]
        public async Task RenameDepartment_Implicit_Fails()
        {
            var college = _db.AddCollege("College of Law", flat: true);
            var implicitDept = await _db.Context.Departments.SingleAsync();

            var result = await Service.RenameDepartmentAsync(implicitDept.DepartmentID, "Anything", null);

            Assert.False(result.Ok);
        }

        [Fact]
        public async Task RenameProgram_DuplicateInDepartment_Fails()
        {
            var dept = _db.AddDepartment(_db.AddCollege("CCIT"), "IT&IS");
            _db.AddProgram(dept, "BSIT");
            var bsis = _db.AddProgram(dept, "BSIS");

            var result = await Service.RenameProgramAsync(bsis.ProgramID, "bsit", null);

            Assert.False(result.Ok);
            Assert.Contains("already has", result.Error);
        }

        [Fact]
        public async Task RetireProgram_Empty_SetsInactiveAndRetiredAt()
        {
            var program = _db.AddProgram(_db.AddDepartment(_db.AddCollege("CCIT"), "IT&IS"), "BSIT");

            var result = await Service.SetProgramActiveAsync(program.ProgramID, false);

            Assert.True(result.Ok);
            var saved = await _db.NewContext().Programs.SingleAsync();
            Assert.False(saved.IsActive);
            Assert.NotNull(saved.RetiredAt);
        }

        [Fact]
        public async Task RetireProgram_WithInactiveUserPlaced_Fails()
        {
            var college = _db.AddCollege("CCIT");
            var dept = _db.AddDepartment(college, "IT&IS");
            var program = _db.AddProgram(dept, "BSIT");
            _db.AddUser("Student Pending", college.CollegeID, dept.DepartmentID, program.ProgramID, isActive: false);

            var result = await Service.SetProgramActiveAsync(program.ProgramID, false);

            Assert.False(result.Ok);
            Assert.Contains("1 user", result.Error);
            Assert.True((await _db.NewContext().Programs.SingleAsync()).IsActive);
        }

        [Fact]
        public async Task RestoreProgram_ClearsRetiredAt()
        {
            var program = _db.AddProgram(_db.AddDepartment(_db.AddCollege("CCIT"), "IT&IS"), "BSIT");
            await Service.SetProgramActiveAsync(program.ProgramID, false);

            var result = await Service.SetProgramActiveAsync(program.ProgramID, true);

            Assert.True(result.Ok);
            var saved = await _db.NewContext().Programs.SingleAsync();
            Assert.True(saved.IsActive);
            Assert.Null(saved.RetiredAt);
        }

        [Fact]
        public async Task RestoreProgram_UnderRetiredDepartment_Fails()
        {
            var dept = _db.AddDepartment(_db.AddCollege("CCIT"), "IT&IS");
            var program = _db.AddProgram(dept, "BSIT");
            await Service.SetProgramActiveAsync(program.ProgramID, false);
            await Service.SetDepartmentActiveAsync(dept.DepartmentID, false);

            var result = await Service.SetProgramActiveAsync(program.ProgramID, true);

            Assert.False(result.Ok);
            Assert.Contains("Restore", result.Error);
        }

        [Fact]
        public async Task RetireDepartment_WithActiveProgram_Fails()
        {
            var dept = _db.AddDepartment(_db.AddCollege("CCIT"), "IT&IS");
            _db.AddProgram(dept, "BSIT");

            var result = await Service.SetDepartmentActiveAsync(dept.DepartmentID, false);

            Assert.False(result.Ok);
            Assert.Contains("active program", result.Error);
        }

        [Fact]
        public async Task RetireDepartment_WithPlacedFaculty_Fails()
        {
            var college = _db.AddCollege("CCIT");
            var dept = _db.AddDepartment(college, "IT&IS");
            _db.AddUser("Faculty", college.CollegeID, dept.DepartmentID);

            var result = await Service.SetDepartmentActiveAsync(dept.DepartmentID, false);

            Assert.False(result.Ok);
            Assert.Contains("1 user", result.Error);
        }

        [Fact]
        public async Task RetireDepartment_Implicit_Fails()
        {
            _db.AddCollege("College of Law", flat: true);
            var implicitDept = await _db.Context.Departments.SingleAsync();

            var result = await Service.SetDepartmentActiveAsync(implicitDept.DepartmentID, false);

            Assert.False(result.Ok);
            Assert.Contains("college", result.Error);
        }

        [Fact]
        public async Task RestoreDepartment_UnderRetiredCollege_Fails()
        {
            var college = _db.AddCollege("CCIT");
            var dept = _db.AddDepartment(college, "IT&IS");
            await Service.SetDepartmentActiveAsync(dept.DepartmentID, false);
            await Service.SetCollegeActiveAsync(college.CollegeID, false);

            var result = await Service.SetDepartmentActiveAsync(dept.DepartmentID, true);

            Assert.False(result.Ok);
            Assert.Contains("Restore", result.Error);
        }

        [Fact]
        public async Task RetireCollege_WithActiveDepartment_Fails()
        {
            var college = _db.AddCollege("CCIT");
            _db.AddDepartment(college, "IT&IS");

            var result = await Service.SetCollegeActiveAsync(college.CollegeID, false);

            Assert.False(result.Ok);
            Assert.Contains("active department", result.Error);
        }

        [Fact]
        public async Task RetireCollege_Flat_WithActiveProgram_Fails()
        {
            _db.AddCollege("College of Law", flat: true);
            var implicitDept = await _db.Context.Departments.SingleAsync();
            _db.AddProgram(implicitDept, "Juris Doctor");
            var college = await _db.Context.Colleges.SingleAsync();

            var result = await Service.SetCollegeActiveAsync(college.CollegeID, false);

            Assert.False(result.Ok);
            Assert.Contains("active program", result.Error);
        }

        [Fact]
        public async Task RetireCollege_WithPlacedDean_Fails()
        {
            var college = _db.AddCollege("CCIT");
            _db.AddUser("Dean", college.CollegeID);

            var result = await Service.SetCollegeActiveAsync(college.CollegeID, false);

            Assert.False(result.Ok);
            Assert.Contains("1 user", result.Error);
        }

        [Fact]
        public async Task RetireAndRestoreCollege_Flat_CarriesItsImplicitDepartment()
        {
            var college = _db.AddCollege("College of Law", flat: true);

            Assert.True((await Service.SetCollegeActiveAsync(college.CollegeID, false)).Ok);
            Assert.False((await _db.NewContext().Departments.SingleAsync()).IsActive);

            Assert.True((await Service.SetCollegeActiveAsync(college.CollegeID, true)).Ok);
            var dept = await _db.NewContext().Departments.SingleAsync();
            Assert.True(dept.IsActive);
            Assert.Null(dept.RetiredAt);
        }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cd /c/EduConnect/src && dotnet test EduConnect.Tests 2>&1 | tail -5`
Expected: build FAILS — `'HierarchyService' does not contain a definition for 'RenameCollegeAsync'`.

- [ ] **Step 3: Extend the interface**

In `IHierarchyService`, after `AddProgramAsync` add:
```csharp

        Task<HierarchyResult> RenameCollegeAsync(int id, string? name, string? shortName);
        Task<HierarchyResult> RenameDepartmentAsync(int id, string? name, string? shortName);
        Task<HierarchyResult> RenameProgramAsync(int id, string? name, string? shortName);

        // active = false retires, active = true restores.
        Task<HierarchyResult> SetCollegeActiveAsync(int id, bool active);
        Task<HierarchyResult> SetDepartmentActiveAsync(int id, bool active);
        Task<HierarchyResult> SetProgramActiveAsync(int id, bool active);
```

- [ ] **Step 4: Implement**

In `HierarchyService`, insert before `// ─── Helpers ───`:
```csharp
        public async Task<HierarchyResult> RenameCollegeAsync(int id, string? name, string? shortName)
        {
            name = Clean(name);
            shortName = Clean(shortName);
            var invalid = ValidateNames(name, CollegeNameMax, shortName);
            if (invalid != null)
                return invalid;

            var college = await _context.Colleges
                .Include(c => c.Departments)
                .FirstOrDefaultAsync(c => c.CollegeID == id);
            if (college == null)
                return HierarchyResult.Fail("College not found.");

            var lower = name!.ToLower();
            if (await _context.Colleges.AnyAsync(c => c.CollegeID != id && c.Name.ToLower() == lower))
                return HierarchyResult.Fail($"A college named \"{name}\" already exists.");

            college.Name = name;
            college.ShortName = shortName;
            college.UpdatedAt = DateTime.Now;

            // The implicit department mirrors its college's name.
            foreach (var dept in college.Departments.Where(d => d.IsImplicit))
            {
                dept.Name = name;
                dept.UpdatedAt = DateTime.Now;
            }

            await _context.SaveChangesAsync();
            return HierarchyResult.Success;
        }

        public async Task<HierarchyResult> RenameDepartmentAsync(int id, string? name, string? shortName)
        {
            name = Clean(name);
            shortName = Clean(shortName);
            var invalid = ValidateNames(name, NameMax, shortName);
            if (invalid != null)
                return invalid;

            var dept = await _context.Departments
                .Include(d => d.College)
                    .ThenInclude(c => c.Departments)
                .FirstOrDefaultAsync(d => d.DepartmentID == id);
            if (dept == null)
                return HierarchyResult.Fail("Department not found.");
            if (dept.IsImplicit)
                return HierarchyResult.Fail(
                    $"{dept.College.Name} has no departments; rename the college instead.");

            var lower = name!.ToLower();
            if (dept.College.Departments.Any(d => d.DepartmentID != id && d.Name.ToLower() == lower))
                return HierarchyResult.Fail(
                    $"{dept.College.Name} already has a department named \"{name}\".");

            dept.Name = name;
            dept.ShortName = shortName;
            dept.UpdatedAt = DateTime.Now;
            await _context.SaveChangesAsync();
            return HierarchyResult.Success;
        }

        public async Task<HierarchyResult> RenameProgramAsync(int id, string? name, string? shortName)
        {
            name = Clean(name);
            shortName = Clean(shortName);
            var invalid = ValidateNames(name, NameMax, shortName);
            if (invalid != null)
                return invalid;

            var program = await _context.Programs
                .Include(p => p.Department)
                    .ThenInclude(d => d.College)
                .Include(p => p.Department)
                    .ThenInclude(d => d.Programs)
                .FirstOrDefaultAsync(p => p.ProgramID == id);
            if (program == null)
                return HierarchyResult.Fail("Program not found.");

            var lower = name!.ToLower();
            if (program.Department.Programs.Any(p => p.ProgramID != id && p.Name.ToLower() == lower))
                return HierarchyResult.Fail(
                    $"{DisplayName(program.Department)} already has a program named \"{name}\".");

            program.Name = name;
            program.ShortName = shortName;
            program.UpdatedAt = DateTime.Now;
            await _context.SaveChangesAsync();
            return HierarchyResult.Success;
        }

        public async Task<HierarchyResult> SetCollegeActiveAsync(int id, bool active)
        {
            var college = await _context.Colleges
                .Include(c => c.Departments)
                    .ThenInclude(d => d.Programs)
                .FirstOrDefaultAsync(c => c.CollegeID == id);
            if (college == null)
                return HierarchyResult.Fail("College not found.");

            var implicitDept = college.Departments.FirstOrDefault(d => d.IsImplicit);

            if (!active)
            {
                var placed = await _context.Users.CountAsync(u => u.CollegeID == id);
                if (placed > 0)
                    return PlacedUsersFail(placed, college.Name, "college");

                var activeDepts = college.Departments.Count(d => !d.IsImplicit && d.IsActive);
                if (activeDepts > 0)
                    return HierarchyResult.Fail(
                        $"{college.Name} has {activeDepts} active department(s). Retire them first.");

                var activePrograms = implicitDept?.Programs.Count(p => p.IsActive) ?? 0;
                if (activePrograms > 0)
                    return HierarchyResult.Fail(
                        $"{college.Name} has {activePrograms} active program(s). Retire them first.");
            }

            SetActive(college, active);
            if (implicitDept != null)
                SetActive(implicitDept, active);

            await _context.SaveChangesAsync();
            return HierarchyResult.Success;
        }

        public async Task<HierarchyResult> SetDepartmentActiveAsync(int id, bool active)
        {
            var dept = await _context.Departments
                .Include(d => d.College)
                .Include(d => d.Programs)
                .FirstOrDefaultAsync(d => d.DepartmentID == id);
            if (dept == null)
                return HierarchyResult.Fail("Department not found.");
            if (dept.IsImplicit)
                return HierarchyResult.Fail(
                    $"{dept.College.Name} has no departments; retire or restore the college instead.");

            if (active)
            {
                if (!dept.College.IsActive)
                    return HierarchyResult.Fail($"Restore {dept.College.Name} first.");
            }
            else
            {
                var placed = await _context.Users.CountAsync(u => u.DepartmentID == id);
                if (placed > 0)
                    return PlacedUsersFail(placed, dept.Name, "department");

                var activePrograms = dept.Programs.Count(p => p.IsActive);
                if (activePrograms > 0)
                    return HierarchyResult.Fail(
                        $"{dept.Name} has {activePrograms} active program(s). Retire them first.");
            }

            SetActive(dept, active);
            await _context.SaveChangesAsync();
            return HierarchyResult.Success;
        }

        public async Task<HierarchyResult> SetProgramActiveAsync(int id, bool active)
        {
            var program = await _context.Programs
                .Include(p => p.Department)
                    .ThenInclude(d => d.College)
                .FirstOrDefaultAsync(p => p.ProgramID == id);
            if (program == null)
                return HierarchyResult.Fail("Program not found.");

            if (active)
            {
                if (!program.Department.College.IsActive)
                    return HierarchyResult.Fail($"Restore {program.Department.College.Name} first.");
                if (!program.Department.IsActive)
                    return HierarchyResult.Fail($"Restore {program.Department.Name} first.");
            }
            else
            {
                // Every placed user counts, pending and deactivated ones
                // included: approval would otherwise land a student in a
                // retired program.
                var placed = await _context.Users.CountAsync(u => u.ProgramID == id);
                if (placed > 0)
                    return PlacedUsersFail(placed, program.Name, "program");
            }

            SetActive(program, active);
            await _context.SaveChangesAsync();
            return HierarchyResult.Success;
        }
```
and add to the helpers section:
```csharp
        private static HierarchyResult PlacedUsersFail(int count, string name, string level) =>
            HierarchyResult.Fail(
                $"{count} user{(count == 1 ? " is" : "s are")} placed in {name}. " +
                $"Move {(count == 1 ? "them" : "them all")} to another {level} first.");

        private static void SetActive(College college, bool active)
        {
            college.IsActive = active;
            college.RetiredAt = active ? null : DateTime.Now;
            college.UpdatedAt = DateTime.Now;
        }

        private static void SetActive(Department dept, bool active)
        {
            dept.IsActive = active;
            dept.RetiredAt = active ? null : DateTime.Now;
            dept.UpdatedAt = DateTime.Now;
        }

        private static void SetActive(AcademicProgram program, bool active)
        {
            program.IsActive = active;
            program.RetiredAt = active ? null : DateTime.Now;
            program.UpdatedAt = DateTime.Now;
        }
```
Note the "1 user" wording: `PlacedUsersFail(1, ...)` produces "1 user is placed in …", which the tests match with `Contains("1 user")`.

- [ ] **Step 5: Run the tests to verify they pass**

Run: `cd /c/EduConnect/src && dotnet test EduConnect.Tests 2>&1 | tail -1`
Expected: `Passed!  - Failed: 0, Passed: 30`.

- [ ] **Step 6: Commit**

```bash
cd /c/EduConnect && git add src && git commit -m "Let the hierarchy service rename, retire and restore

Nothing can be retired while users are placed in it or its children
are still active, and nothing is restored under a retired parent.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 3: Academic Structure admin page

**Files:**
- Create: `src/EduConnect.Web/Controllers/AcademicStructureController.cs`, `src/EduConnect.Web/Views/AcademicStructure/Index.cshtml`, `src/EduConnect.Web/Views/AcademicStructure/_ProgramList.cshtml`
- Modify: `src/EduConnect.Web/Views/Shared/_SidebarContent.cshtml:60-63`, `src/EduConnect.Web/Views/Admin/Departments.cshtml:2,22,24,28,256-261`

**Interfaces:**
- Consumes: `IHierarchyService` (Tasks 1–2).
- Produces: routes `GET /AcademicStructure`, `POST /AcademicStructure/{AddCollege,AddDepartment,AddProgram,RenameCollege,RenameDepartment,RenameProgram,SetCollegeActive,SetDepartmentActive,SetProgramActive}`.

- [ ] **Step 1: Write the controller**

`src/EduConnect.Web/Controllers/AcademicStructureController.cs`:
```csharp
using EduConnect.Web.Data;
using EduConnect.Web.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EduConnect.Web.Controllers
{
    // Admin management of the College > Department > Program hierarchy.
    // Every rule lives in IHierarchyService; this only relays its result.
    public class AcademicStructureController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly IHierarchyService _hierarchy;

        public AcademicStructureController(
            ApplicationDbContext context,
            IHierarchyService hierarchy)
        {
            _context = context;
            _hierarchy = hierarchy;
        }

        private bool IsAdmin() =>
            HttpContext.Session.GetString("RoleName") == RoleNames.Administrator;

        public async Task<IActionResult> Index()
        {
            if (!IsAdmin())
                return RedirectToAction("Login", "Account");

            ViewBag.CollegeUsers = await _context.Users
                .Where(u => u.CollegeID != null)
                .GroupBy(u => u.CollegeID!.Value)
                .ToDictionaryAsync(g => g.Key, g => g.Count());
            ViewBag.DepartmentUsers = await _context.Users
                .Where(u => u.DepartmentID != null)
                .GroupBy(u => u.DepartmentID!.Value)
                .ToDictionaryAsync(g => g.Key, g => g.Count());
            ViewBag.ProgramUsers = await _context.Users
                .Where(u => u.ProgramID != null)
                .GroupBy(u => u.ProgramID!.Value)
                .ToDictionaryAsync(g => g.Key, g => g.Count());

            return View(await _hierarchy.GetTreeAsync(includeRetired: true));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddCollege(string? name, string? shortName, bool hasDepartments)
        {
            if (!IsAdmin())
                return RedirectToAction("Login", "Account");
            return Done(await _hierarchy.AddCollegeAsync(name, shortName, hasDepartments),
                $"Added \"{name?.Trim()}\".");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddDepartment(int collegeId, string? name, string? shortName)
        {
            if (!IsAdmin())
                return RedirectToAction("Login", "Account");
            return Done(await _hierarchy.AddDepartmentAsync(collegeId, name, shortName),
                $"Added \"{name?.Trim()}\".", $"college-{collegeId}");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddProgram(int departmentId, int collegeId, string? name, string? shortName)
        {
            if (!IsAdmin())
                return RedirectToAction("Login", "Account");
            return Done(await _hierarchy.AddProgramAsync(departmentId, name, shortName),
                $"Added \"{name?.Trim()}\".", $"college-{collegeId}");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RenameCollege(int id, string? name, string? shortName)
        {
            if (!IsAdmin())
                return RedirectToAction("Login", "Account");
            return Done(await _hierarchy.RenameCollegeAsync(id, name, shortName),
                "College updated.", $"college-{id}");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RenameDepartment(int id, int collegeId, string? name, string? shortName)
        {
            if (!IsAdmin())
                return RedirectToAction("Login", "Account");
            return Done(await _hierarchy.RenameDepartmentAsync(id, name, shortName),
                "Department updated.", $"college-{collegeId}");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RenameProgram(int id, int collegeId, string? name, string? shortName)
        {
            if (!IsAdmin())
                return RedirectToAction("Login", "Account");
            return Done(await _hierarchy.RenameProgramAsync(id, name, shortName),
                "Program updated.", $"college-{collegeId}");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SetCollegeActive(int id, bool active)
        {
            if (!IsAdmin())
                return RedirectToAction("Login", "Account");
            return Done(await _hierarchy.SetCollegeActiveAsync(id, active),
                active ? "College restored." : "College retired.", $"college-{id}");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SetDepartmentActive(int id, int collegeId, bool active)
        {
            if (!IsAdmin())
                return RedirectToAction("Login", "Account");
            return Done(await _hierarchy.SetDepartmentActiveAsync(id, active),
                active ? "Department restored." : "Department retired.", $"college-{collegeId}");
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SetProgramActive(int id, int collegeId, bool active)
        {
            if (!IsAdmin())
                return RedirectToAction("Login", "Account");
            return Done(await _hierarchy.SetProgramActiveAsync(id, active),
                active ? "Program restored." : "Program retired.", $"college-{collegeId}");
        }

        private IActionResult Done(HierarchyResult result, string successMessage, string? anchor = null)
        {
            if (result.Ok)
                TempData["Success"] = successMessage;
            else
                TempData["Error"] = result.Error;

            var url = Url.Action("Index") + (anchor == null ? "" : "#" + anchor);
            return Redirect(url!);
        }
    }
}
```

- [ ] **Step 2: Write the view**

`src/EduConnect.Web/Views/AcademicStructure/Index.cshtml`:
```cshtml
@model List<EduConnect.Web.Models.College>
@{
    ViewData["Title"] = "Academic Structure";
    var collegeUsers = ViewBag.CollegeUsers as Dictionary<int, int> ?? new();
    var departmentUsers = ViewBag.DepartmentUsers as Dictionary<int, int> ?? new();
    var programUsers = ViewBag.ProgramUsers as Dictionary<int, int> ?? new();
    int Count(Dictionary<int, int> d, int id) => d.TryGetValue(id, out var n) ? n : 0;
}

<div class="d-flex justify-content-between align-items-center mb-4">
    <div>
        <h4 class="fw-bold mb-1">
            <i class="bi bi-bank me-2 text-primary"></i>
            Academic Structure
        </h4>
        <small class="text-muted">
            @Model.Count(c => c.IsActive) colleges ·
            @Model.SelectMany(c => c.Departments).Count(d => d.IsActive && !d.IsImplicit) departments ·
            @Model.SelectMany(c => c.Departments).SelectMany(d => d.Programs).Count(p => p.IsActive) programs
        </small>
    </div>
    <a href="/Admin" class="btn btn-outline-secondary btn-sm">
        <i class="bi bi-arrow-left me-2"></i>Back
    </a>
</div>

@if (TempData["Success"] != null)
{
    <div class="alert alert-success alert-dismissible mb-4">
        <i class="bi bi-check-circle me-2"></i>@TempData["Success"]
        <button type="button" class="btn-close" data-bs-dismiss="alert"></button>
    </div>
}
@if (TempData["Error"] != null)
{
    <div class="alert alert-danger alert-dismissible mb-4">
        <i class="bi bi-exclamation-triangle me-2"></i>@TempData["Error"]
        <button type="button" class="btn-close" data-bs-dismiss="alert"></button>
    </div>
}

<!-- ─── ADD COLLEGE ─────────────────── -->
<details class="card border-0 shadow-sm mb-4">
    <summary class="card-body fw-semibold" style="cursor:pointer">
        <i class="bi bi-plus-lg me-1"></i>Add college
    </summary>
    <div class="card-body pt-0">
        <form asp-action="AddCollege" method="post" class="row g-2 align-items-end">
            @Html.AntiForgeryToken()
            <div class="col-12 col-md-6">
                <label class="form-label small">Name</label>
                <input name="name" class="form-control" maxlength="100" required />
            </div>
            <div class="col-6 col-md-2">
                <label class="form-label small">Short code</label>
                <input name="shortName" class="form-control" maxlength="20" />
            </div>
            <div class="col-6 col-md-2">
                <div class="form-check mb-2">
                    <input type="checkbox" name="hasDepartments" value="true" id="hasDepartments"
                           class="form-check-input" checked />
                    <input type="hidden" name="hasDepartments" value="false" />
                    <label for="hasDepartments" class="form-check-label small">Has departments</label>
                </div>
            </div>
            <div class="col-12 col-md-2">
                <button type="submit" class="btn btn-primary w-100">Add</button>
            </div>
        </form>
        <p class="text-muted small mt-2 mb-0">
            Untick "Has departments" for a college whose programs sit directly under it
            (like Law or Nursing). That choice cannot be changed later.
        </p>
    </div>
</details>

<!-- ─── COLLEGES ────────────────────── -->
@foreach (var college in Model)
{
    var implicitDept = college.Departments.FirstOrDefault(d => d.IsImplicit);
    var isFlat = implicitDept != null;
    var visibleDepts = college.Departments.Where(d => !d.IsImplicit).ToList();

    <div class="card border-0 shadow-sm mb-3 @(college.IsActive ? "" : "opacity-75")" id="college-@college.CollegeID">
        <div class="card-header bg-white d-flex flex-wrap justify-content-between align-items-center gap-2">
            <div>
                <span class="fw-bold">@college.Name</span>
                @if (!string.IsNullOrEmpty(college.ShortName))
                {
                    <span class="badge bg-light text-dark border ms-1">@college.ShortName</span>
                }
                @if (isFlat)
                {
                    <span class="badge bg-light text-muted border ms-1">No departments</span>
                }
                @if (!college.IsActive)
                {
                    <span class="badge bg-secondary ms-1">Retired</span>
                }
                <span class="text-muted small ms-2">@Count(collegeUsers, college.CollegeID) users</span>
            </div>
            <div class="d-flex gap-1">
                <details class="position-relative">
                    <summary class="btn btn-sm btn-outline-primary" title="Rename"><i class="bi bi-pencil"></i></summary>
                    <form asp-action="RenameCollege" asp-route-id="@college.CollegeID" method="post"
                          class="card card-body shadow position-absolute end-0 mt-1" style="z-index:5;width:320px">
                        @Html.AntiForgeryToken()
                        <input name="name" value="@college.Name" class="form-control form-control-sm mb-2" maxlength="100" required />
                        <input name="shortName" value="@college.ShortName" class="form-control form-control-sm mb-2" maxlength="20" placeholder="Short code" />
                        <button type="submit" class="btn btn-sm btn-primary">Save</button>
                    </form>
                </details>
                <form asp-action="SetCollegeActive" asp-route-id="@college.CollegeID" method="post"
                      onsubmit="return confirm(@Json.Serialize((college.IsActive ? "Retire " : "Restore ") + college.Name + "?"));">
                    @Html.AntiForgeryToken()
                    <input type="hidden" name="active" value="@((!college.IsActive).ToString().ToLower())" />
                    @if (college.IsActive)
                    {
                        <button type="submit" class="btn btn-sm btn-outline-secondary" title="Retire"><i class="bi bi-archive"></i></button>
                    }
                    else
                    {
                        <button type="submit" class="btn btn-sm btn-outline-success" title="Restore"><i class="bi bi-arrow-counterclockwise"></i></button>
                    }
                </form>
            </div>
        </div>

        <div class="card-body">
            @if (isFlat)
            {
                @await Html.PartialAsync("_ProgramList", (implicitDept!, college.CollegeID, programUsers))
            }
            else
            {
                @foreach (var dept in visibleDepts)
                {
                    <div class="border rounded p-2 mb-2 @(dept.IsActive ? "" : "opacity-75")">
                        <div class="d-flex flex-wrap justify-content-between align-items-center gap-2 mb-1">
                            <div>
                                <span class="fw-semibold small">@dept.Name</span>
                                @if (!string.IsNullOrEmpty(dept.ShortName))
                                {
                                    <span class="badge bg-light text-dark border ms-1">@dept.ShortName</span>
                                }
                                @if (!dept.IsActive)
                                {
                                    <span class="badge bg-secondary ms-1">Retired</span>
                                }
                                <span class="text-muted small ms-2">@Count(departmentUsers, dept.DepartmentID) users</span>
                            </div>
                            <div class="d-flex gap-1">
                                <details class="position-relative">
                                    <summary class="btn btn-sm btn-outline-primary" title="Rename"><i class="bi bi-pencil"></i></summary>
                                    <form asp-action="RenameDepartment" asp-route-id="@dept.DepartmentID" method="post"
                                          class="card card-body shadow position-absolute end-0 mt-1" style="z-index:5;width:320px">
                                        @Html.AntiForgeryToken()
                                        <input type="hidden" name="collegeId" value="@college.CollegeID" />
                                        <input name="name" value="@dept.Name" class="form-control form-control-sm mb-2" maxlength="150" required />
                                        <input name="shortName" value="@dept.ShortName" class="form-control form-control-sm mb-2" maxlength="20" placeholder="Short code" />
                                        <button type="submit" class="btn btn-sm btn-primary">Save</button>
                                    </form>
                                </details>
                                <form asp-action="SetDepartmentActive" asp-route-id="@dept.DepartmentID" method="post"
                                      onsubmit="return confirm(@Json.Serialize((dept.IsActive ? "Retire " : "Restore ") + dept.Name + "?"));">
                                    @Html.AntiForgeryToken()
                                    <input type="hidden" name="collegeId" value="@college.CollegeID" />
                                    <input type="hidden" name="active" value="@((!dept.IsActive).ToString().ToLower())" />
                                    @if (dept.IsActive)
                                    {
                                        <button type="submit" class="btn btn-sm btn-outline-secondary" title="Retire"><i class="bi bi-archive"></i></button>
                                    }
                                    else
                                    {
                                        <button type="submit" class="btn btn-sm btn-outline-success" title="Restore"><i class="bi bi-arrow-counterclockwise"></i></button>
                                    }
                                </form>
                            </div>
                        </div>
                        @await Html.PartialAsync("_ProgramList", (dept, college.CollegeID, programUsers))
                    </div>
                }

                @if (college.IsActive)
                {
                    <details class="mt-2">
                        <summary class="small text-primary" style="cursor:pointer">
                            <i class="bi bi-plus-lg me-1"></i>Add department
                        </summary>
                        <form asp-action="AddDepartment" method="post" class="row g-2 mt-1">
                            @Html.AntiForgeryToken()
                            <input type="hidden" name="collegeId" value="@college.CollegeID" />
                            <div class="col-12 col-md-7"><input name="name" class="form-control form-control-sm" maxlength="150" placeholder="Department name" required /></div>
                            <div class="col-6 col-md-3"><input name="shortName" class="form-control form-control-sm" maxlength="20" placeholder="Short code" /></div>
                            <div class="col-6 col-md-2"><button type="submit" class="btn btn-sm btn-primary w-100">Add</button></div>
                        </form>
                    </details>
                }
            }
        </div>
    </div>
}

<p class="text-muted small mt-3 mb-0">
    <i class="bi bi-info-circle me-1"></i>
    Items are retired rather than deleted, so existing users and announcements keep pointing
    at them. Something can only be retired once nobody is placed in it and everything under
    it is retired.
</p>
```

`src/EduConnect.Web/Views/AcademicStructure/_ProgramList.cshtml`:
```cshtml
@model (EduConnect.Web.Models.Department Dept, int CollegeID, Dictionary<int, int> ProgramUsers)
@{
    var dept = Model.Dept;
    int Count(int id) => Model.ProgramUsers.TryGetValue(id, out var n) ? n : 0;
}

<ul class="list-unstyled mb-1 ms-2">
    @foreach (var program in dept.Programs)
    {
        <li class="d-flex flex-wrap justify-content-between align-items-center gap-2 py-1 border-bottom @(program.IsActive ? "" : "opacity-75")">
            <div class="small">
                @program.Name
                @if (!string.IsNullOrEmpty(program.ShortName))
                {
                    <span class="badge bg-light text-dark border ms-1">@program.ShortName</span>
                }
                @if (!program.IsActive)
                {
                    <span class="badge bg-secondary ms-1">Retired</span>
                }
                <span class="text-muted ms-2">@Count(program.ProgramID) users</span>
            </div>
            <div class="d-flex gap-1">
                <details class="position-relative">
                    <summary class="btn btn-sm btn-outline-primary py-0" title="Rename"><i class="bi bi-pencil"></i></summary>
                    <form asp-controller="AcademicStructure" asp-action="RenameProgram" asp-route-id="@program.ProgramID" method="post"
                          class="card card-body shadow position-absolute end-0 mt-1" style="z-index:5;width:320px">
                        @Html.AntiForgeryToken()
                        <input type="hidden" name="collegeId" value="@Model.CollegeID" />
                        <input name="name" value="@program.Name" class="form-control form-control-sm mb-2" maxlength="150" required />
                        <input name="shortName" value="@program.ShortName" class="form-control form-control-sm mb-2" maxlength="20" placeholder="Short code" />
                        <button type="submit" class="btn btn-sm btn-primary">Save</button>
                    </form>
                </details>
                <form asp-controller="AcademicStructure" asp-action="SetProgramActive" asp-route-id="@program.ProgramID" method="post"
                      onsubmit="return confirm(@Json.Serialize((program.IsActive ? "Retire " : "Restore ") + program.Name + "?"));">
                    @Html.AntiForgeryToken()
                    <input type="hidden" name="collegeId" value="@Model.CollegeID" />
                    <input type="hidden" name="active" value="@((!program.IsActive).ToString().ToLower())" />
                    @if (program.IsActive)
                    {
                        <button type="submit" class="btn btn-sm btn-outline-secondary py-0" title="Retire"><i class="bi bi-archive"></i></button>
                    }
                    else
                    {
                        <button type="submit" class="btn btn-sm btn-outline-success py-0" title="Restore"><i class="bi bi-arrow-counterclockwise"></i></button>
                    }
                </form>
            </div>
        </li>
    }
</ul>

@if (dept.IsActive && dept.College?.IsActive != false)
{
    <details class="ms-2">
        <summary class="small text-primary" style="cursor:pointer">
            <i class="bi bi-plus-lg me-1"></i>Add program
        </summary>
        <form asp-controller="AcademicStructure" asp-action="AddProgram" method="post" class="row g-2 mt-1">
            @Html.AntiForgeryToken()
            <input type="hidden" name="departmentId" value="@dept.DepartmentID" />
            <input type="hidden" name="collegeId" value="@Model.CollegeID" />
            <div class="col-12 col-md-7"><input name="name" class="form-control form-control-sm" maxlength="150" placeholder="Program name" required /></div>
            <div class="col-6 col-md-3"><input name="shortName" class="form-control form-control-sm" maxlength="20" placeholder="Short code" /></div>
            <div class="col-6 col-md-2"><button type="submit" class="btn btn-sm btn-primary w-100">Add</button></div>
        </form>
    </details>
}
```
Note: `GetTreeAsync` loads without `Department.College`, so `dept.College` is null in the partial; the `?.IsActive != false` treats null as active. A retired college still hides "Add program" because its departments are shown under a retired card — the service refuses the add anyway with "is retired".

- [ ] **Step 3: Sidebar and tag page labels**

In `_SidebarContent.cshtml`, replace the admin `Departments` link:
```cshtml
        <a href="/Admin/Departments" class="ec-side-link">
            <i class="bi bi-diagram-3"></i>
            Departments
        </a>
```
with:
```cshtml
        <a href="/AcademicStructure" class="ec-side-link">
            <i class="bi bi-bank"></i>
            Academic Structure
        </a>
        <a href="/Admin/Departments" class="ec-side-link">
            <i class="bi bi-tags"></i>
            Tags &amp; Offices
        </a>
```
In `Views/Admin/Departments.cshtml`: set `ViewData["Title"] = "Tags & Offices";`, change the `<h4>` text `Departments` to `Tags &amp; Offices`, the subtitle `@departments.Count departments` to `@departments.Count tags`, the button text `Add Department` to `Add Tag`, and replace the footnote paragraph text with:
```
    Tags drive the current announcement feed: School Wide and the non-academic
    offices. Colleges, departments and programs are managed under Academic
    Structure. Tags are retired rather than deleted.
```

- [ ] **Step 4: Build and verify in the browser**

Stop the dev server if running.
```bash
cd /c/EduConnect/src && dotnet build EduConnect.Web 2>&1 | grep -E " error |Build succeeded" | sort -u
```
Expected: `Build succeeded.`

Start `educonnect-web`, log in as admin (credentials in project memory) and check, reading results with `get_page_text`/`javascript_tool`:
1. `/AcademicStructure` lists 9 colleges; College of Law shows "No departments" with "Juris Doctor" and an "Add program" link but no "Add department".
2. Add program "Test Program X" to College of Law → success banner, it appears; rename it to "Test Program Y" → shows new name; retire it → "Retired" badge; restore → active.
3. Retire CCIT → error banner "16 users are placed in College of Computing and Information Technology…".
4. Retire "Department of Biology" → error "has 1 active program(s)".
5. Log in as `uitest.faculty` and open `/AcademicStructure` → redirected to `/Account/Login`.
Take one screenshot of the page for the report. Then remove the test program row you created:
```bash
sqlcmd -S 'localhost\SQLEXPRESS' -d EduConnectDB -E -C -b -Q "DELETE FROM Programs WHERE Name = 'Test Program Y' AND NOT EXISTS (SELECT 1 FROM Users WHERE ProgramID = Programs.ProgramID)"
```
Stop the server.

- [ ] **Step 5: Commit**

```bash
cd /c/EduConnect && git add src && git commit -m "Add an Academic Structure page for the admin

Colleges, departments and programs can be added, renamed, retired and
restored from one tree. The old Departments page is now Tags & Offices.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 4: Placement service

**Files:**
- Create: `src/EduConnect.Web/Services/IPlacementService.cs`, `src/EduConnect.Web/Services/PlacementService.cs`
- Test: `src/EduConnect.Tests/PlacementServiceTests.cs`
- Modify: `src/EduConnect.Web/Program.cs`

**Interfaces:**
- Consumes: `HierarchyResult` (Task 1), `RoleNames`.
- Produces:
  - `IPlacementService.ApplyAsync(User user, string roleName, int? collegeId, int? departmentId, int? programId) : Task<HierarchyResult>` — sets `user.CollegeID/DepartmentID/ProgramID` per role; does **not** save (caller saves).
  - `static Expression<Func<User, bool>> PlacementService.NeedsPlacement` — true for Students/Student Pending without a program, Chairperson/Faculty without a department, Dean without a college.

- [ ] **Step 1: Write the failing tests**

`src/EduConnect.Tests/PlacementServiceTests.cs`:
```csharp
using EduConnect.Web;
using EduConnect.Web.Models;
using EduConnect.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace EduConnect.Tests
{
    public class PlacementServiceTests : IDisposable
    {
        private readonly TestDb _db = new();
        private readonly College _ccit;
        private readonly Department _itis;
        private readonly AcademicProgram _bsit;
        private readonly College _cos;
        private readonly College _law;

        public PlacementServiceTests()
        {
            _ccit = _db.AddCollege("CCIT");
            _itis = _db.AddDepartment(_ccit, "IT&IS");
            _bsit = _db.AddProgram(_itis, "BSIT");
            _cos = _db.AddCollege("College of Science");
            _law = _db.AddCollege("College of Law", flat: true);
        }

        public void Dispose() => _db.Dispose();

        private PlacementService Service => new(_db.Context);

        private static User NewUser() => new();

        private Department LawDept => _db.Context.Departments.Single(d => d.CollegeID == _law.CollegeID);

        [Fact]
        public async Task Student_Program_SetsAllThreeLevels()
        {
            var user = NewUser();

            var result = await Service.ApplyAsync(user, RoleNames.Student, null, null, _bsit.ProgramID);

            Assert.True(result.Ok);
            Assert.Equal(_ccit.CollegeID, user.CollegeID);
            Assert.Equal(_itis.DepartmentID, user.DepartmentID);
            Assert.Equal(_bsit.ProgramID, user.ProgramID);
        }

        [Fact]
        public async Task StudentPending_WithoutProgram_Fails()
        {
            var result = await Service.ApplyAsync(NewUser(), RoleNames.StudentPending, _ccit.CollegeID, _itis.DepartmentID, null);

            Assert.False(result.Ok);
            Assert.Contains("program", result.Error);
        }

        [Fact]
        public async Task Student_RetiredProgram_Fails()
        {
            _bsit.IsActive = false;
            _db.Context.SaveChanges();

            var result = await Service.ApplyAsync(NewUser(), RoleNames.Student, null, null, _bsit.ProgramID);

            Assert.False(result.Ok);
        }

        [Fact]
        public async Task Student_ProgramUnderRetiredCollege_Fails()
        {
            _ccit.IsActive = false;
            _db.Context.SaveChanges();

            var result = await Service.ApplyAsync(NewUser(), RoleNames.Student, null, null, _bsit.ProgramID);

            Assert.False(result.Ok);
        }

        [Fact]
        public async Task Student_ProgramWinsOverMismatchedCollege()
        {
            var user = NewUser();

            var result = await Service.ApplyAsync(user, RoleNames.Student, _cos.CollegeID, null, _bsit.ProgramID);

            Assert.True(result.Ok);
            Assert.Equal(_ccit.CollegeID, user.CollegeID);
        }

        [Fact]
        public async Task Faculty_Department_SetsCollegeAndDepartment()
        {
            var user = NewUser();
            user.ProgramID = _bsit.ProgramID;

            var result = await Service.ApplyAsync(user, RoleNames.Faculty, null, _itis.DepartmentID, _bsit.ProgramID);

            Assert.True(result.Ok);
            Assert.Equal(_ccit.CollegeID, user.CollegeID);
            Assert.Equal(_itis.DepartmentID, user.DepartmentID);
            Assert.Null(user.ProgramID);
        }

        [Fact]
        public async Task Faculty_FlatCollege_ResolvesImplicitDepartment()
        {
            var user = NewUser();

            var result = await Service.ApplyAsync(user, RoleNames.Faculty, _law.CollegeID, null, null);

            Assert.True(result.Ok);
            Assert.Equal(_law.CollegeID, user.CollegeID);
            Assert.Equal(LawDept.DepartmentID, user.DepartmentID);
        }

        [Fact]
        public async Task Faculty_StructuredCollegeWithoutDepartment_Fails()
        {
            var result = await Service.ApplyAsync(NewUser(), RoleNames.Faculty, _ccit.CollegeID, null, null);

            Assert.False(result.Ok);
            Assert.Contains("department", result.Error);
        }

        [Fact]
        public async Task Chairperson_FlatCollege_Fails()
        {
            var result = await Service.ApplyAsync(NewUser(), RoleNames.Chairperson, _law.CollegeID, null, null);

            Assert.False(result.Ok);
            Assert.Contains("no departments", result.Error);
        }

        [Fact]
        public async Task Chairperson_RetiredDepartment_Fails()
        {
            _itis.IsActive = false;
            _db.Context.SaveChanges();

            var result = await Service.ApplyAsync(NewUser(), RoleNames.Chairperson, null, _itis.DepartmentID, null);

            Assert.False(result.Ok);
        }

        [Fact]
        public async Task Dean_IgnoresPostedDepartmentAndProgram()
        {
            var user = NewUser();

            var result = await Service.ApplyAsync(user, RoleNames.Dean, _cos.CollegeID, _itis.DepartmentID, _bsit.ProgramID);

            Assert.True(result.Ok);
            Assert.Equal(_cos.CollegeID, user.CollegeID);
            Assert.Null(user.DepartmentID);
            Assert.Null(user.ProgramID);
        }

        [Fact]
        public async Task Dean_WithoutCollege_Fails()
        {
            var result = await Service.ApplyAsync(NewUser(), RoleNames.Dean, null, null, null);

            Assert.False(result.Ok);
            Assert.Contains("college", result.Error);
        }

        [Fact]
        public async Task Staff_ClearsExistingPlacement()
        {
            var user = NewUser();
            user.CollegeID = _ccit.CollegeID;
            user.DepartmentID = _itis.DepartmentID;
            user.ProgramID = _bsit.ProgramID;

            var result = await Service.ApplyAsync(user, RoleNames.Staff, _ccit.CollegeID, _itis.DepartmentID, _bsit.ProgramID);

            Assert.True(result.Ok);
            Assert.Null(user.CollegeID);
            Assert.Null(user.DepartmentID);
            Assert.Null(user.ProgramID);
        }

        [Fact]
        public async Task NeedsPlacement_FlagsOnlyUsersMissingTheirRequiredLevel()
        {
            _db.AddUser(RoleNames.Student, _ccit.CollegeID);                                   // no program → flagged
            _db.AddUser(RoleNames.Student, _ccit.CollegeID, _itis.DepartmentID, _bsit.ProgramID);
            _db.AddUser(RoleNames.Faculty, _ccit.CollegeID);                                   // no department → flagged
            _db.AddUser(RoleNames.Dean, _ccit.CollegeID);
            _db.AddUser(RoleNames.Dean);                                                        // no college → flagged
            _db.AddUser(RoleNames.Staff);

            var flagged = await _db.NewContext().Users
                .Include(u => u.Role)
                .Where(PlacementService.NeedsPlacement)
                .Select(u => u.Role.RoleName)
                .ToListAsync();

            Assert.Equal(new[] { RoleNames.Dean, RoleNames.Faculty, RoleNames.Student },
                flagged.OrderBy(r => r));
        }
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `cd /c/EduConnect/src && dotnet test EduConnect.Tests 2>&1 | tail -5`
Expected: build FAILS — `The type or namespace name 'PlacementService' could not be found`.

- [ ] **Step 3: Write the interface**

`src/EduConnect.Web/Services/IPlacementService.cs`:
```csharp
using EduConnect.Web.Models;

namespace EduConnect.Web.Services
{
    // The only writer of Users.CollegeID / DepartmentID / ProgramID.
    public interface IPlacementService
    {
        // Sets the user's placement for the given role from the most
        // specific level that role uses, deriving the levels above it.
        // Mutates `user` only; the caller saves.
        Task<HierarchyResult> ApplyAsync(User user, string roleName,
            int? collegeId, int? departmentId, int? programId);
    }
}
```

- [ ] **Step 4: Write the implementation**

`src/EduConnect.Web/Services/PlacementService.cs`:
```csharp
using System.Linq.Expressions;
using EduConnect.Web.Data;
using EduConnect.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace EduConnect.Web.Services
{
    public class PlacementService : IPlacementService
    {
        private readonly ApplicationDbContext _context;

        public PlacementService(ApplicationDbContext context)
        {
            _context = context;
        }

        // Users whose role needs a placement they do not have yet.
        // Needs Role loaded or translatable (use inside an EF query).
        public static readonly Expression<Func<User, bool>> NeedsPlacement = u =>
            ((u.Role.RoleName == RoleNames.Student ||
              u.Role.RoleName == RoleNames.StudentPending) && u.ProgramID == null) ||
            ((u.Role.RoleName == RoleNames.Chairperson ||
              u.Role.RoleName == RoleNames.Faculty) && u.DepartmentID == null) ||
            (u.Role.RoleName == RoleNames.Dean && u.CollegeID == null);

        public async Task<HierarchyResult> ApplyAsync(User user, string roleName,
            int? collegeId, int? departmentId, int? programId)
        {
            switch (roleName)
            {
                case RoleNames.Student:
                case RoleNames.StudentPending:
                    return await PlaceStudentAsync(user, programId);

                case RoleNames.Chairperson:
                case RoleNames.Faculty:
                    return await PlaceStaffAsync(user, roleName, collegeId, departmentId);

                case RoleNames.Dean:
                    return await PlaceDeanAsync(user, collegeId);

                default:
                    // Administrators and Staff are never placed.
                    Set(user, null, null, null);
                    return HierarchyResult.Success;
            }
        }

        private async Task<HierarchyResult> PlaceStudentAsync(User user, int? programId)
        {
            if (programId == null)
                return HierarchyResult.Fail("Choose the student's program.");

            var program = await _context.Programs
                .Include(p => p.Department)
                    .ThenInclude(d => d.College)
                .FirstOrDefaultAsync(p => p.ProgramID == programId);

            if (program == null || !program.IsActive ||
                !program.Department.IsActive || !program.Department.College.IsActive)
                return HierarchyResult.Fail("Choose an active program.");

            Set(user, program.Department.CollegeID, program.DepartmentID, program.ProgramID);
            return HierarchyResult.Success;
        }

        private async Task<HierarchyResult> PlaceStaffAsync(User user, string roleName,
            int? collegeId, int? departmentId)
        {
            Department? dept = null;

            if (departmentId != null)
                dept = await _context.Departments
                    .Include(d => d.College)
                    .FirstOrDefaultAsync(d => d.DepartmentID == departmentId);
            else if (collegeId != null)
                // A college without departments: its one implicit department.
                dept = await _context.Departments
                    .Include(d => d.College)
                    .FirstOrDefaultAsync(d => d.CollegeID == collegeId && d.IsImplicit);

            if (dept == null || !dept.IsActive || !dept.College.IsActive)
                return HierarchyResult.Fail("Choose an active department.");

            if (dept.IsImplicit && roleName == RoleNames.Chairperson)
                return HierarchyResult.Fail(
                    $"{dept.College.Name} has no departments, so it has no Chairperson. " +
                    "Make this user its Dean or Faculty instead.");

            Set(user, dept.CollegeID, dept.DepartmentID, null);
            return HierarchyResult.Success;
        }

        private async Task<HierarchyResult> PlaceDeanAsync(User user, int? collegeId)
        {
            if (collegeId == null)
                return HierarchyResult.Fail("Choose the Dean's college.");

            var college = await _context.Colleges.FindAsync(collegeId.Value);
            if (college == null || !college.IsActive)
                return HierarchyResult.Fail("Choose an active college.");

            Set(user, college.CollegeID, null, null);
            return HierarchyResult.Success;
        }

        private static void Set(User user, int? collegeId, int? departmentId, int? programId)
        {
            user.CollegeID = collegeId;
            user.DepartmentID = departmentId;
            user.ProgramID = programId;
        }
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `cd /c/EduConnect/src && dotnet test EduConnect.Tests 2>&1 | tail -1`
Expected: `Passed!  - Failed: 0, Passed: 44`.

- [ ] **Step 6: Register and commit**

In `Program.cs`, after the `IHierarchyService` registration add:
```csharp
builder.Services.AddScoped<EduConnect.Web.Services.IPlacementService, EduConnect.Web.Services.PlacementService>();
```
```bash
cd /c/EduConnect/src && dotnet build EduConnect.Web 2>&1 | grep -E " error |Build succeeded" | sort -u
cd /c/EduConnect && git add src && git commit -m "Add the placement service

One place decides where a user sits in the hierarchy for their role,
deriving college and department from the most specific level given.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```
Expected: `Build succeeded.`

---

### Task 5: Placement fields on the admin user forms

**Files:**
- Modify: `src/EduConnect.Web/ViewModel/AdminUserFormViewModel.cs`, `src/EduConnect.Web/Controllers/AdminController.cs` (ctor lines 13-25; AddUser 397-514; EditUser 519-646), `src/EduConnect.Web/Views/Admin/AddUser.cshtml`, `src/EduConnect.Web/Views/Admin/EditUser.cshtml`
- Create: `src/EduConnect.Web/Views/Admin/_PlacementFields.cshtml`

**Interfaces:**
- Consumes: `IPlacementService.ApplyAsync` (Task 4), `IHierarchyService.GetTreeAsync` (Task 1).
- Produces: `AdminUserFormViewModel.CollegeID/DepartmentID/ProgramID` (`int?`), `AdminUserFormViewModel.Hierarchy` (`List<College>`); `AdminController.PopulateUserFormAsync(AdminUserFormViewModel)`; ModelState key `"Placement"`.

- [ ] **Step 1: Extend the view model**

In `AdminUserFormViewModel`, after `public int DepartmentTagID { get; set; }` add:
```csharp

        // Academic placement; which levels apply depends on the role.
        // IPlacementService validates and derives the rest.
        public int? CollegeID { get; set; }
        public int? DepartmentID { get; set; }
        public int? ProgramID { get; set; }
```
and after `public List<SelectListItem> Departments { get; set; } = new();` add:
```csharp
        public List<College> Hierarchy { get; set; } = new();
```
Change the tag field's error message to `"Feed tag is required"`.

- [ ] **Step 2: Inject the services and add the form helper**

Replace the `AdminController` fields and constructor with:
```csharp
        private readonly ApplicationDbContext _context;
        private readonly IEmailService _emailService;
        private readonly ILogger<AdminController> _logger;
        private readonly IHierarchyService _hierarchy;
        private readonly IPlacementService _placement;

        public AdminController(
            ApplicationDbContext context,
            IEmailService emailService,
            ILogger<AdminController> logger,
            IHierarchyService hierarchy,
            IPlacementService placement)
        {
            _context = context;
            _emailService = emailService;
            _logger = logger;
            _hierarchy = hierarchy;
            _placement = placement;
        }
```
Add this helper just above the `// GET: /Admin/AddUser` banner:
```csharp
        // Dropdown data for the Add/Edit User forms.
        private async Task PopulateUserFormAsync(AdminUserFormViewModel model)
        {
            model.Roles = (await _context.Roles.ToListAsync())
                .Select(r => new SelectListItem(r.RoleName, r.RoleID.ToString()))
                .ToList();
            model.Departments = (await _context.DepartmentTags
                .Where(d => d.IsActive)
                .ToListAsync())
                .Select(d => new SelectListItem(
                    $"{d.ShortName} — {d.TagName}", d.TagID.ToString()))
                .ToList();
            model.Hierarchy = await _hierarchy.GetTreeAsync(includeRetired: false);
        }
```
Replace every inline `model.Roles = ...; model.Departments = ...;` pair in AddUser GET/POST and EditUser POST, and the `Roles = ..., Departments = ...` initializers in AddUser GET and EditUser GET, with a call to `await PopulateUserFormAsync(model);` (for the GET object initializers: build the model without those two properties, then call the helper before `return View(model);`).

- [ ] **Step 3: Apply placement in AddUser POST**

In `AddUser(AdminUserFormViewModel model)`, replace everything from `if (!ModelState.IsValid)` through `await _context.SaveChangesAsync();` (the first save, after `_context.Users.Add(user);`) with:
```csharp
            var roleName = await _context.Roles
                .Where(r => r.RoleID == model.RoleID)
                .Select(r => r.RoleName)
                .FirstOrDefaultAsync();
            if (roleName == null)
                ModelState.AddModelError("RoleID", "Choose a valid role.");

            var adminID = int.Parse(HttpContext.Session.GetString("UserID"));

            var user = new User
            {
                FirstName = model.FirstName,
                LastName = model.LastName,
                Email = model.Email,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(model.Password ?? ""),
                StudentID = model.StudentID,
                RoleID = model.RoleID,
                IsActive = model.IsActive,
                VerificationStatus = "Verified",
                VerifiedByID = adminID,
                VerifiedAt = DateTime.Now,
                CreatedAt = DateTime.Now
            };

            if (roleName != null)
            {
                var placement = await _placement.ApplyAsync(
                    user, roleName, model.CollegeID, model.DepartmentID, model.ProgramID);
                if (!placement.Ok)
                    ModelState.AddModelError("Placement", placement.Error!);
            }

            if (!ModelState.IsValid)
            {
                await PopulateUserFormAsync(model);
                return View(model);
            }

            _context.Users.Add(user);
            await _context.SaveChangesAsync();
```
(The `UserDepartments` add, welcome email and redirect that follow stay unchanged.)

- [ ] **Step 4: Apply placement in EditUser**

In `EditUser(int id)` GET, add to the model initializer:
```csharp
                CollegeID = user.CollegeID,
                DepartmentID = user.DepartmentID,
                ProgramID = user.ProgramID,
```
In `EditUser(int id, AdminUserFormViewModel model)` POST, immediately after the `if (user == null) { ... }` block insert:
```csharp
            var roleName = await _context.Roles
                .Where(r => r.RoleID == model.RoleID)
                .Select(r => r.RoleName)
                .FirstOrDefaultAsync();
            if (roleName == null)
            {
                ModelState.AddModelError("RoleID", "Choose a valid role.");
            }
            else
            {
                var placement = await _placement.ApplyAsync(
                    user, roleName, model.CollegeID, model.DepartmentID, model.ProgramID);
                if (!placement.Ok)
                    ModelState.AddModelError("Placement", placement.Error!);
            }

            if (!ModelState.IsValid)
            {
                // Nothing has been saved; the tracked user is discarded
                // with this request.
                await PopulateUserFormAsync(model);
                return View(model);
            }
```

- [ ] **Step 5: Write the placement partial**

`src/EduConnect.Web/Views/Admin/_PlacementFields.cshtml`:
```cshtml
@model EduConnect.Web.ViewModels.AdminUserFormViewModel

<div class="col-12" id="placementFields">
    <label class="form-label fw-semibold mb-1">Placement</label>
    <div class="text-muted small mb-2" id="placementHint"></div>
    <div class="row g-2">
        <div class="col-md-4" data-level="college">
            <select asp-for="CollegeID" class="form-select">
                <option value="">— College —</option>
                @foreach (var c in Model.Hierarchy)
                {
                    <option value="@c.CollegeID" data-flat="@(c.Departments.Any(d => d.IsImplicit) ? "1" : "0")">@c.Name</option>
                }
            </select>
        </div>
        <div class="col-md-4" data-level="department">
            <select asp-for="DepartmentID" class="form-select">
                <option value="">— Department —</option>
                @foreach (var c in Model.Hierarchy)
                {
                    foreach (var d in c.Departments.Where(d => !d.IsImplicit))
                    {
                        <option value="@d.DepartmentID" data-college="@c.CollegeID">@d.Name</option>
                    }
                }
            </select>
        </div>
        <div class="col-md-4" data-level="program">
            <select asp-for="ProgramID" class="form-select">
                <option value="">— Program —</option>
                @foreach (var c in Model.Hierarchy)
                {
                    foreach (var d in c.Departments)
                    {
                        foreach (var p in d.Programs)
                        {
                            <option value="@p.ProgramID" data-college="@c.CollegeID"
                                    data-department="@(d.IsImplicit ? "" : d.DepartmentID.ToString())">@p.Name</option>
                        }
                    }
                }
            </select>
        </div>
    </div>
    @Html.ValidationMessage("Placement", null, new { @class = "text-danger small d-block mt-1" })
</div>

<script>
    (function () {
        // Which levels each role uses — mirrors PlacementService.
        const roles = @Json.Serialize(new
        {
            dean = RoleNames.Dean,
            chair = RoleNames.Chairperson,
            faculty = RoleNames.Faculty,
            student = RoleNames.Student,
            pending = RoleNames.StudentPending
        });
        const roleSelect = document.getElementById('RoleID');
        const college = document.getElementById('CollegeID');
        const dept = document.getElementById('DepartmentID');
        const program = document.getElementById('ProgramID');
        const hint = document.getElementById('placementHint');
        const box = level => document.querySelector('#placementFields [data-level="' + level + '"]');

        function roleName() {
            const opt = roleSelect.options[roleSelect.selectedIndex];
            return opt ? opt.text.trim() : '';
        }

        function filter(select, keep) {
            for (const opt of select.options) {
                if (!opt.value) continue;
                const show = keep(opt);
                opt.hidden = !show;
                opt.disabled = !show;
            }
            const current = select.options[select.selectedIndex];
            if (current && current.disabled) select.value = '';
        }

        function refresh() {
            const role = roleName();
            const isStudent = role === roles.student || role === roles.pending;
            const isStaff = role === roles.chair || role === roles.faculty;
            const isDean = role === roles.dean;
            const collegeOpt = college.options[college.selectedIndex];
            const flat = collegeOpt && collegeOpt.dataset.flat === '1';

            box('college').hidden = !(isStudent || isStaff || isDean);
            box('department').hidden = !((isStudent || isStaff) && !flat);
            box('program').hidden = !isStudent;

            if (!roleSelect.value) {
                hint.textContent = 'Choose a role first.';
            } else if (!(isStudent || isStaff || isDean)) {
                hint.textContent = 'Administrators and Staff are not placed in a college.';
            } else if (role === roles.chair && flat) {
                hint.textContent = 'This college has no departments, so it has no Chairperson.';
            } else if (isDean) {
                hint.textContent = 'A Dean is placed in a college.';
            } else if (isStaff) {
                hint.textContent = flat ? 'Placed in the college as a whole.' : 'Choose the department.';
            } else {
                hint.textContent = 'Choose the student\'s program.';
            }

            filter(dept, o => o.dataset.college === college.value);
            if (flat) dept.value = '';
            filter(program, o => o.dataset.college === college.value &&
                (flat || !dept.value || o.dataset.department === dept.value));
        }

        roleSelect.addEventListener('change', refresh);
        college.addEventListener('change', refresh);
        dept.addEventListener('change', refresh);
        refresh();
    })();
</script>
```

- [ ] **Step 6: Use the partial in both forms**

In `AddUser.cshtml` and `EditUser.cshtml`:
- Change the tag field's label text from `Department` to `Feed tag` and add below its `<span asp-validation-for=...>`:
  ```cshtml
                      <div class="form-text">School Wide or an office tag; the announcement feed uses it until targeting moves to colleges.</div>
  ```
- Immediately after that field's closing `</div>` (the `col-md-6` holding `DepartmentTagID`), insert:
  ```cshtml
                  @await Html.PartialAsync("_PlacementFields", Model)
  ```

- [ ] **Step 7: Build and verify in the browser**

Stop the server; run
```bash
cd /c/EduConnect/src && dotnet build EduConnect.Web 2>&1 | grep -E " error |Build succeeded" | sort -u && dotnet test EduConnect.Tests 2>&1 | tail -1
```
Expected: `Build succeeded.` and `Passed!  - Failed: 0, Passed: 44`.

Start the server, log in as admin and check:
1. `/Admin/EditUser/17` (uitest.faculty, Faculty): the Placement row shows College + Department only; choosing College of Law hides Department and shows "Placed in the college as a whole."; choosing CCIT lists only CS and IT&IS.
2. Place user 17 in CCIT → IT&IS and save → success; `sqlcmd` shows `CollegeID`=CCIT's ID, `DepartmentID`=IT&IS's ID, `ProgramID` NULL.
3. Switch the Role select to Chairperson, college College of Law, save → form returns with "College of Law has no departments, so it has no Chairperson…" under Placement and nothing saved (re-query: still Faculty in IT&IS).
4. `/Admin/EditUser/<a Student's id>` (pick one: `SELECT TOP 1 u.UserID FROM Users u JOIN Roles r ON r.RoleID=u.RoleID WHERE r.RoleName='Student'`): Placement shows College, Department, Program; the Program list narrows to the chosen department's programs.
5. Restore user 17 afterwards to its pre-test state (Faculty, placement as before: CollegeID NULL, DepartmentID NULL — it had only the ALL tag) with:
   ```bash
   sqlcmd -S 'localhost\SQLEXPRESS' -d EduConnectDB -E -C -b -Q "UPDATE Users SET CollegeID=NULL, DepartmentID=NULL, ProgramID=NULL, RoleID=(SELECT RoleID FROM Roles WHERE RoleName='Faculty') WHERE UserID=17"
   ```
Stop the server.

- [ ] **Step 8: Commit**

```bash
cd /c/EduConnect && git add src && git commit -m "Let the admin place users in the academic hierarchy

The Add and Edit User forms gain a college, department and program
picker that shows only the levels the chosen role uses.

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

### Task 6: Placement on the Users list

**Files:**
- Modify: `src/EduConnect.Web/Controllers/AdminController.cs` (`Users` action, ~350-392), `src/EduConnect.Web/Views/Admin/Users.cshtml` (filters ~55-116, header ~131, cell ~189-200)

**Interfaces:**
- Consumes: `PlacementService.NeedsPlacement` (Task 4).
- Produces: query parameter `filterPlacement=missing`; `ViewBag.NeedsPlacementCount`.

- [ ] **Step 1: Controller**

Change the `Users` signature to add `string? filterPlacement`, extend the query's includes with:
```csharp
                .Include(u => u.College)
                .Include(u => u.Department)
                .Include(u => u.AcademicProgram)
```
after the status filter add:
```csharp
            if (filterPlacement == "missing")
                query = query.Where(PlacementService.NeedsPlacement);
```
and before `return View();` add:
```csharp
            ViewBag.FilterPlacement = filterPlacement;
            ViewBag.NeedsPlacementCount = await _context.Users
                .Where(PlacementService.NeedsPlacement)
                .CountAsync();
```

- [ ] **Step 2: View — filter**

In `Users.cshtml` change the search column class `col-12 col-md-4` to `col-12 col-md-3`, the role and status columns `col-6 col-md-3` to `col-6 col-md-2`, and insert after the status column:
```cshtml
            <div class="col-6 col-md-3">
                <select name="filterPlacement" class="form-select">
                    <option value="">Any placement</option>
                    <option value="missing" selected="@(ViewBag.FilterPlacement == "missing")">
                        Needs placement
                    </option>
                </select>
            </div>
```
(the buttons column stays `col-md-2`: 3+2+2+3+2 = 12). Above the filters card add:
```cshtml
@if ((int)(ViewBag.NeedsPlacementCount ?? 0) > 0 && ViewBag.FilterPlacement != "missing")
{
    <div class="alert alert-warning d-flex justify-content-between align-items-center mb-4">
        <span>
            <i class="bi bi-diagram-3 me-2"></i>
            @ViewBag.NeedsPlacementCount user(s) still need a college, department or program.
        </span>
        <a href="/Admin/Users?filterPlacement=missing" class="btn btn-sm btn-warning">Show them</a>
    </div>
}
```

- [ ] **Step 3: View — column**

Change the header `<th>Department</th>` to `<th>Placement</th>` and replace the Department cell (`<!-- Department -->` `<td>` … `</td>`) with:
```cshtml
                                <!-- Placement -->
                                <td class="small">
                                    @if (user.AcademicProgram != null)
                                    {
                                        <div>@(user.AcademicProgram.ShortName ?? user.AcademicProgram.Name)</div>
                                    }
                                    else if (user.Department != null && !user.Department.IsImplicit)
                                    {
                                        <div>@(user.Department.ShortName ?? user.Department.Name)</div>
                                    }
                                    @if (user.College != null)
                                    {
                                        <div class="text-muted" style="font-size:11px">@(user.College.ShortName ?? user.College.Name)</div>
                                    }
                                    else if (user.AcademicProgram == null && user.Department == null)
                                    {
                                        <span class="text-muted">—</span>
                                    }
                                    @if (dept?.DepartmentTag != null)
                                    {
                                        <span class="badge bg-light text-dark border mt-1" title="Feed tag">
                                            @dept.DepartmentTag.ShortName
                                        </span>
                                    }
                                </td>
```
(`dept` is the existing primary-`UserDepartments` local defined at the top of the row loop.)

- [ ] **Step 4: Build and verify**

Stop the server; `cd /c/EduConnect/src && dotnet build EduConnect.Web 2>&1 | grep -E " error |Build succeeded" | sort -u && dotnet test EduConnect.Tests 2>&1 | tail -1` → `Build succeeded.`, `Passed! … Passed: 44`.

Start the server, log in as admin, open `/Admin/Users`:
1. The warning banner shows a count equal to
   `sqlcmd … -Q "SELECT COUNT(*) FROM Users u JOIN Roles r ON r.RoleID=u.RoleID WHERE (r.RoleName IN ('Student','Student Pending') AND u.ProgramID IS NULL) OR (r.RoleName IN ('Chairperson','Faculty') AND u.DepartmentID IS NULL) OR (r.RoleName='Dean' AND u.CollegeID IS NULL)"`.
2. "Show them" lists exactly that many users; each shows its college short code (e.g. CCIT) plus its feed tag badge.
3. Admin and Staff rows are not in the filtered list.
Stop the server.

- [ ] **Step 5: Commit**

```bash
cd /c/EduConnect && git add src && git commit -m "Show placement on the user list and flag who still needs it

Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>"
```

---

## After this plan

Report: test count, what the admin can now do, how many local users still need placement (the banner count), and that `DepartmentTags` still drive the feed until Plan 4. Then write Plan 3 (registration cascade, student profile program editor, required program at sign-up).
