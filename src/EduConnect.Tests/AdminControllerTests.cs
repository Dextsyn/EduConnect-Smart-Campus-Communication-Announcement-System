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
