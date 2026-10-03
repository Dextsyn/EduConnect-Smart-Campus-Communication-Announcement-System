using EduConnect.Web;
using EduConnect.Web.Controllers;
using EduConnect.Web.Models;
using EduConnect.Web.Services;
using EduConnect.Web.ViewModels;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Logging.Abstractions;

namespace EduConnect.Tests
{
    public class AdminControllerTests : IDisposable
    {
        private readonly TestDb _db = new();
        private readonly FakeSession _session = new();
        private readonly DepartmentTag _schoolWide;

        public AdminControllerTests()
        {
            _schoolWide = _db.AddTag(AudienceService.SchoolWide);
            var admin = _db.AddUser(RoleNames.Administrator);
            _session.SetString("UserID", admin.UserID.ToString());
        }

        public void Dispose() => _db.Dispose();

        private AdminController Controller()
        {
            _session.SetString("RoleName", RoleNames.Administrator);
            var http = FakeSession.HttpContextWith(_session);
            var controller = new AdminController(
                _db.Context, new FakeEmailService(), NullLogger<AdminController>.Instance,
                new HierarchyService(_db.Context), new PlacementService(_db.Context),
                new AuditService(_db.Context, new HttpContextAccessor { HttpContext = http }));
            controller.ControllerContext = new ControllerContext { HttpContext = http };
            controller.TempData = new TempDataDictionary(controller.ControllerContext.HttpContext, new NullTempDataProvider());
            return controller;
        }

        private static AdminUserFormViewModel FormFor(User user, bool canPostSchoolWide) => new()
        {
            UserID = user.UserID,
            FirstName = user.FirstName,
            LastName = user.LastName,
            Email = user.Email,
            RoleID = user.RoleID,
            CollegeID = user.CollegeID,
            DepartmentID = user.DepartmentID,
            ProgramID = user.ProgramID,
            IsActive = user.IsActive,
            CanPostSchoolWide = canPostSchoolWide
        };

        private int[] TagsOf(User user) =>
            _db.NewContext().UserDepartments.Where(ud => ud.UserID == user.UserID)
                .Select(ud => ud.TagID).OrderBy(id => id).ToArray();

        private (College College, Department Dept) Placement()
        {
            var college = _db.AddCollege("College of Computing and Information Technology");
            return (college, _db.AddDepartment(college, "Information Technology"));
        }

        [Fact]
        public async Task EditUser_GrantsSchoolWide()
        {
            var (college, dept) = Placement();
            var faculty = _db.AddUser(RoleNames.Faculty, college.CollegeID, dept.DepartmentID);

            await Controller().EditUser(faculty.UserID, FormFor(faculty, canPostSchoolWide: true));

            Assert.Equal(new[] { _schoolWide.TagID }, TagsOf(faculty));
        }

        [Fact]
        public async Task EditUser_RevokesSchoolWide_KeepsRetiredTags()
        {
            var (college, dept) = Placement();
            var faculty = _db.AddUser(RoleNames.Faculty, college.CollegeID, dept.DepartmentID);
            var legacy = _db.AddTag("CCIT");
            legacy.IsActive = false;
            _db.Context.SaveChanges();
            _db.TagUser(faculty, legacy);
            _db.TagUser(faculty, _schoolWide, primary: false);

            await Controller().EditUser(faculty.UserID, FormFor(faculty, canPostSchoolWide: false));

            Assert.Equal(new[] { legacy.TagID }, TagsOf(faculty));
        }

        // ─── Bulk move ─────────────────────────

        private User Saved(User u) => _db.NewContext().Users.Single(x => x.UserID == u.UserID);

        [Fact]
        public async Task BulkMove_ToProgram_EachRoleLandsAtItsLevel()
        {
            var (oldCollege, oldDept) = Placement();
            var oldProgram = _db.AddProgram(oldDept, "BS Old");
            var student = _db.AddUser(RoleNames.Student, oldCollege.CollegeID, oldDept.DepartmentID, oldProgram.ProgramID);
            var faculty = _db.AddUser(RoleNames.Faculty, oldCollege.CollegeID, oldDept.DepartmentID);
            var dean = _db.AddUser(RoleNames.Dean, oldCollege.CollegeID);

            var newCollege = _db.AddCollege("College of Science");
            var newDept = _db.AddDepartment(newCollege, "Biology");
            var newProgram = _db.AddProgram(newDept, "BS Biology");

            await Controller().BulkMove(new[] { student.UserID, faculty.UserID, dean.UserID },
                null, null, newProgram.ProgramID);

            var s = Saved(student);
            Assert.Equal((newCollege.CollegeID, newDept.DepartmentID, newProgram.ProgramID),
                (s.CollegeID!.Value, s.DepartmentID!.Value, s.ProgramID!.Value));
            var f = Saved(faculty);
            Assert.Equal((newCollege.CollegeID, newDept.DepartmentID, (int?)null),
                (f.CollegeID!.Value, f.DepartmentID!.Value, f.ProgramID));
            var d = Saved(dean);
            Assert.Equal((newCollege.CollegeID, (int?)null), (d.CollegeID!.Value, d.DepartmentID));

            Assert.Equal(3, _db.NewContext().AuditLogs.Count(l => l.Action == "Move"));
        }

        [Fact]
        public async Task BulkMove_CollegeOnly_SkipsStudentsAndStaff()
        {
            var (oldCollege, oldDept) = Placement();
            var oldProgram = _db.AddProgram(oldDept, "BS Old");
            var student = _db.AddUser(RoleNames.Student, oldCollege.CollegeID, oldDept.DepartmentID, oldProgram.ProgramID);
            var dean = _db.AddUser(RoleNames.Dean, oldCollege.CollegeID);
            var staff = _db.AddUser(RoleNames.Staff);
            var newCollege = _db.AddCollege("College of Science");
            _db.AddDepartment(newCollege, "Biology");

            var controller = Controller();
            await controller.BulkMove(new[] { student.UserID, dean.UserID, staff.UserID },
                newCollege.CollegeID, null, null);

            Assert.Equal(oldProgram.ProgramID, Saved(student).ProgramID);
            Assert.Equal(newCollege.CollegeID, Saved(dean).CollegeID);
            var error = (string)controller.TempData["Error"]!;
            Assert.Contains("Skipped 2", error);
            Assert.Contains("Choose a program", error);
            Assert.Contains("Moved 1", (string)controller.TempData["Success"]!);
        }

        [Fact]
        public async Task BulkMove_RetiredDestination_MovesNoOne()
        {
            var (oldCollege, oldDept) = Placement();
            var oldProgram = _db.AddProgram(oldDept, "BS Old");
            var student = _db.AddUser(RoleNames.Student, oldCollege.CollegeID, oldDept.DepartmentID, oldProgram.ProgramID);
            var newCollege = _db.AddCollege("College of Science");
            var retired = _db.AddProgram(_db.AddDepartment(newCollege, "Biology"), "BS Retired");
            retired.IsActive = false;
            _db.Context.SaveChanges();

            await Controller().BulkMove(new[] { student.UserID }, null, null, retired.ProgramID);

            Assert.Equal(oldProgram.ProgramID, Saved(student).ProgramID);
            Assert.Empty(_db.NewContext().AuditLogs);
        }

        [Fact]
        public async Task BulkMove_ThenOldCollegeCanBeRetired()
        {
            var oldCollege = _db.AddCollege("College of Old Studies", flat: true);
            var oldProgram = _db.AddProgram(_db.NewContext().Departments.Single(d => d.CollegeID == oldCollege.CollegeID), "BS Old");
            var oldDeptId = _db.NewContext().Departments.Single(d => d.CollegeID == oldCollege.CollegeID).DepartmentID;
            var student = _db.AddUser(RoleNames.Student, oldCollege.CollegeID, oldDeptId, oldProgram.ProgramID);
            var newCollege = _db.AddCollege("College of Science");
            var newProgram = _db.AddProgram(_db.AddDepartment(newCollege, "Biology"), "BS Biology");
            var hierarchy = new HierarchyService(_db.Context);

            Assert.False((await hierarchy.SetCollegeActiveAsync(oldCollege.CollegeID, false)).Ok);

            await Controller().BulkMove(new[] { student.UserID }, null, null, newProgram.ProgramID);
            await hierarchy.SetProgramActiveAsync(oldProgram.ProgramID, false);

            Assert.True((await new HierarchyService(_db.NewContext()).SetCollegeActiveAsync(oldCollege.CollegeID, false)).Ok);
        }

        [Fact]
        public async Task EditUser_ShowsSchoolWidePermission()
        {
            var staff = _db.AddUser(RoleNames.Staff);
            _db.TagUser(staff, _schoolWide);

            var result = await Controller().EditUser(staff.UserID);

            var model = Assert.IsType<AdminUserFormViewModel>(Assert.IsType<ViewResult>(result).Model);
            Assert.True(model.CanPostSchoolWide);
        }
    }
}
