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
            Assert.Contains("already has a department", result.Error);
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
            _db.AddCollege("College of Law", flat: true);
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
        public async Task RetireProgram_WithOnlyDeactivatedUserPlaced_Succeeds()
        {
            var college = _db.AddCollege("CCIT");
            var dept = _db.AddDepartment(college, "IT&IS");
            var program = _db.AddProgram(dept, "BSIT");
            // A graduated / deactivated student: verified, no longer active.
            var alumnus = _db.AddUser("Student", college.CollegeID, dept.DepartmentID, program.ProgramID, isActive: false);
            alumnus.VerificationStatus = "Verified";
            _db.Context.SaveChanges();

            var result = await Service.SetProgramActiveAsync(program.ProgramID, false);

            Assert.True(result.Ok);
            // The alumnus keeps pointing at the retired program.
            Assert.Equal(program.ProgramID, (await _db.NewContext().Users.SingleAsync()).ProgramID);
        }

        [Fact]
        public async Task RetireCollege_WithOnlyDeactivatedDeanPlaced_Succeeds()
        {
            var college = _db.AddCollege("CCIT");
            var dean = _db.AddUser("Dean", college.CollegeID, isActive: false);
            dean.VerificationStatus = "Verified";
            _db.Context.SaveChanges();

            var result = await Service.SetCollegeActiveAsync(college.CollegeID, false);

            Assert.True(result.Ok);
        }

        [Fact]
        public async Task AddCollege_SameNameCommittedByAnotherRequestFirst_FailsInsteadOfThrowing()
        {
            _db.BeforeSave.Once = () =>
            {
                using var other = _db.NewContext();
                other.Colleges.Add(new EduConnect.Web.Models.College { Name = "College of Law" });
                other.SaveChanges();
            };

            var result = await Service.AddCollegeAsync("College of Law", null, hasDepartments: true);

            Assert.False(result.Ok);
            Assert.Contains("already", result.Error);
        }

        [Fact]
        public async Task AddProgram_SameNameCommittedByAnotherRequestFirst_FailsInsteadOfThrowing()
        {
            var dept = _db.AddDepartment(_db.AddCollege("CCIT"), "IT&IS");
            _db.BeforeSave.Once = () =>
            {
                using var other = _db.NewContext();
                other.Programs.Add(new EduConnect.Web.Models.AcademicProgram { DepartmentID = dept.DepartmentID, Name = "BSIT" });
                other.SaveChanges();
            };

            var result = await Service.AddProgramAsync(dept.DepartmentID, "BSIT", null);

            Assert.False(result.Ok);
            Assert.Contains("already", result.Error);
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
    }
}
