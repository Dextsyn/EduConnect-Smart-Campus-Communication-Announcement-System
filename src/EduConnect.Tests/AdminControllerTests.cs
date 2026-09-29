using EduConnect.Web;
using EduConnect.Web.Controllers;
using EduConnect.Web.Services;
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

        [Fact]
        public async Task EditDepartment_CollegeTag_CannotReactivate()
        {
            var tag = _db.AddTag("CCIT");
            tag.IsActive = false;
            var college = _db.AddCollege("College of Computing and Information Technology");
            college.LegacyTagID = tag.TagID;
            _db.Context.SaveChanges();

            await Controller().EditDepartment(tag.TagID, new EduConnect.Web.ViewModels.AdminDepartmentFormViewModel
            {
                TagID = tag.TagID,
                TagName = tag.TagName,
                ShortName = tag.ShortName,
                TagTypeID = tag.TagTypeID,
                ColorHex = tag.ColorHex,
                IsActive = true
            });

            Assert.False(_db.NewContext().DepartmentTags.Single(t => t.TagID == tag.TagID).IsActive);
        }
    }
}
