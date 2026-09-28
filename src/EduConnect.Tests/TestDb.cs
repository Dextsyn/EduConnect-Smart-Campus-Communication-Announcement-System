using EduConnect.Web.Data;
using EduConnect.Web.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace EduConnect.Tests
{
    // Runs an action once, just before Context's next save — used to make
    // a race (another request committing first) happen deterministically.
    public sealed class BeforeSaveInterceptor : SaveChangesInterceptor
    {
        public Action? Once { get; set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            var action = Once;
            Once = null;
            action?.Invoke();
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    // One in-memory SQLite database per test: the real EF model with real
    // unique indexes and foreign keys, without needing SQL Server.
    public sealed class TestDb : IDisposable
    {
        private readonly SqliteConnection _connection;

        public ApplicationDbContext Context { get; }

        public BeforeSaveInterceptor BeforeSave { get; } = new();

        public TestDb()
        {
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();
            Context = new ApplicationDbContext(
                new DbContextOptionsBuilder<ApplicationDbContext>()
                    .UseSqlite(_connection)
                    .AddInterceptors(BeforeSave)
                    .Options);
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
