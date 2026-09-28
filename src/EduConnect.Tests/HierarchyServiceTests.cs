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
    }
}
