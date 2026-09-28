using EduConnect.Web;
using EduConnect.Web.Controllers;
using EduConnect.Web.Services;
using EduConnect.Web.ViewModels;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace EduConnect.Tests
{
    public class AccountControllerTests : IDisposable
    {
        private const string OldAvatar = "https://blob.test/profiles/old.png";

        private readonly TestDb _db = new();
        private readonly FakeBlobStorage _blobs = new();
        private readonly FakeSession _session = new();

        public void Dispose() => _db.Dispose();

        private AccountController Controller()
        {
            var controller = new AccountController(
                _db.Context,
                NullLogger<AccountController>.Instance,
                null!, null!, null!, null!,
                _blobs,
                new HierarchyService(_db.Context),
                new PlacementService(_db.Context));
            controller.ControllerContext = new ControllerContext
            {
                HttpContext = FakeSession.HttpContextWith(_session)
            };
            return controller;
        }

        private static IFormFile Png()
        {
            // Real PNG signature so the upload passes the magic-number check.
            var bytes = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0 };
            return new FormFile(new MemoryStream(bytes), 0, bytes.Length, "NewProfilePicture", "new.png")
            {
                Headers = new HeaderDictionary(),
                ContentType = "image/png"
            };
        }

        [Fact]
        public async Task ProfilePost_PlacementFails_KeepsTheOldPictureAndUploadsNothing()
        {
            var college = _db.AddCollege("CCIT");
            var dept = _db.AddDepartment(college, "IT&IS");
            var program = _db.AddProgram(dept, "BSIT");
            var student = _db.AddUser(RoleNames.Student, college.CollegeID, dept.DepartmentID, program.ProgramID);
            student.ProfilePicture = OldAvatar;
            _db.Context.SaveChanges();
            _session.SetString("UserID", student.UserID.ToString());

            // Changed college, program cleared by the picker: placement fails.
            var result = await Controller().Profile(new ProfileViewModel
            {
                NewProfilePicture = Png(),
                CollegeID = college.CollegeID,
                ProgramID = null
            });

            Assert.IsType<ViewResult>(result);
            Assert.Empty(_blobs.Uploaded);
            Assert.Empty(_blobs.Deleted);
            Assert.Equal(OldAvatar, (await _db.NewContext().Users.SingleAsync()).ProfilePicture);
        }

        [Fact]
        public async Task ProfileGet_UserNoLongerExists_ClearsTheSession()
        {
            _session.SetString("UserID", "999");
            _session.SetString(ProgramCompletion.SessionKey, "1");

            var result = await Controller().Profile();

            Assert.IsType<RedirectToActionResult>(result);
            Assert.Empty(_session.Keys);
        }
    }
}
