using EduConnect.Web;
using EduConnect.Web.Controllers;
using EduConnect.Web.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace EduConnect.Tests
{
    // Group Finder is for students, and only a group's creator may dissolve
    // it. Anyone else is turned away with a message, not an error page.
    public class GroupAccessTests : IDisposable
    {
        private readonly TestDb _db = new();
        private readonly FakeSession _session = new();
        private readonly User _creator, _other, _faculty;

        public GroupAccessTests()
        {
            _creator = _db.AddUser(RoleNames.Student);
            _other = _db.AddUser(RoleNames.Student);
            _faculty = _db.AddUser(RoleNames.Faculty);
        }

        public void Dispose() => _db.Dispose();

        private GroupController As(User user, string role)
        {
            _session.SetString("UserID", user.UserID.ToString());
            _session.SetString("RoleName", role);
            var http = FakeSession.HttpContextWith(_session);
            return new GroupController(_db.Context)
            {
                ControllerContext = new ControllerContext { HttpContext = http },
                TempData = new TempDataDictionary(http, new NullTempDataProvider())
            };
        }

        private Group AddGroup()
        {
            var group = new Group
            {
                CreatorID = _creator.UserID,
                Name = "Thesis team",
                Category = "Study Group",
                MaxMembers = 5,
                CreatedAt = DateTime.UtcNow,
                ChatExpiresAt = DateTime.UtcNow.AddDays(7)
            };
            _db.Context.Groups.Add(group);
            _db.Context.SaveChanges();
            _db.Context.GroupMembers.Add(new GroupMember { GroupID = group.GroupID, UserID = _creator.UserID });
            _db.Context.SaveChanges();
            return group;
        }

        private static void AssertSentToIndex(IActionResult result, GroupController controller)
        {
            var redirect = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal(nameof(GroupController.Index), redirect.ActionName);
            Assert.NotNull(controller.TempData["Error"]);
        }

        [Fact]
        public async Task Non_student_opening_create_is_sent_back_with_a_message()
        {
            var controller = As(_faculty, RoleNames.Faculty);
            AssertSentToIndex(await controller.Create(), controller);
        }

        [Fact]
        public async Task Non_student_posting_create_makes_no_group()
        {
            var controller = As(_faculty, RoleNames.Faculty);
            AssertSentToIndex(await controller.Create("Team", "Study Group", null, 5), controller);
            Assert.Empty(_db.NewContext().Groups);
        }

        [Fact]
        public async Task Non_student_cannot_join()
        {
            var group = AddGroup();
            var controller = As(_faculty, RoleNames.Faculty);
            AssertSentToIndex(await controller.Join(group.GroupID), controller);
            Assert.Single(_db.NewContext().GroupMembers.Where(m => m.GroupID == group.GroupID));
        }

        [Fact]
        public async Task Non_student_cannot_leave()
        {
            var group = AddGroup();
            var controller = As(_faculty, RoleNames.Faculty);
            AssertSentToIndex(await controller.Leave(group.GroupID), controller);
        }

        [Fact]
        public async Task Only_the_creator_can_dissolve()
        {
            var group = AddGroup();
            var controller = As(_other, RoleNames.Student);

            var result = await controller.Dissolve(group.GroupID);

            var redirect = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal(nameof(GroupController.Details), redirect.ActionName);
            Assert.NotNull(controller.TempData["Error"]);
            Assert.Equal("Open", _db.NewContext().Groups.Single().Status);
        }
    }
}
