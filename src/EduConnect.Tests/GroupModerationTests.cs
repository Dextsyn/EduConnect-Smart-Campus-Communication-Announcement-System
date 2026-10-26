using EduConnect.Web;
using EduConnect.Web.Controllers;
using EduConnect.Web.Hubs;
using EduConnect.Web.Models;
using EduConnect.Web.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Caching.Memory;

namespace EduConnect.Tests
{
    // Group Finder word moderation: listed words block a new group's name
    // or description, and are starred out of chat messages before they are
    // saved or sent. Words here are made up.
    public class GroupModerationTests : IDisposable
    {
        private const string BlockedCopy =
            "Please remove inappropriate language from the group name or description.";

        private readonly TestDb _db = new();
        private readonly MemoryCache _cache = new(new MemoryCacheOptions());
        private readonly FakeSession _session = new();
        private readonly User _student;

        public GroupModerationTests()
        {
            _db.Context.ModerationWords.AddRange(
                new ModerationWord { Word = "zorbak" },
                new ModerationWord { Word = "flimbo" });
            _db.Context.SaveChanges();

            _student = _db.AddUser(RoleNames.Student);
            _session.SetString("UserID", _student.UserID.ToString());
            _session.SetString("RoleName", RoleNames.Student);
        }

        public void Dispose()
        {
            _cache.Dispose();
            _db.Dispose();
        }

        private ContentModerationService Moderation() => new(_db.Context, _cache);

        private GroupController Controller()
        {
            var http = FakeSession.HttpContextWith(_session);
            return new GroupController(_db.Context, Moderation())
            {
                ControllerContext = new ControllerContext { HttpContext = http },
                TempData = new TempDataDictionary(http, new NullTempDataProvider())
            };
        }

        private (GroupChatHub Hub, FakeHubClients Clients) Hub()
        {
            var clients = new FakeHubClients();
            var hub = new GroupChatHub(_db.Context, Moderation())
            {
                Context = new FakeHubCallerContext(_session),
                Clients = clients
            };
            return (hub, clients);
        }

        private Group AddGroupWithMember()
        {
            var group = new Group
            {
                CreatorID = _student.UserID,
                Name = "Thesis team",
                Category = "Study Group",
                MaxMembers = 5,
                ChatExpiresAt = DateTime.UtcNow.AddDays(7)
            };
            _db.Context.Groups.Add(group);
            _db.Context.SaveChanges();
            _db.Context.GroupMembers.Add(new GroupMember { GroupID = group.GroupID, UserID = _student.UserID });
            _db.Context.SaveChanges();
            return group;
        }

        private static string? BroadcastContent(FakeHubClients clients)
        {
            var (method, args) = Assert.Single(clients.Proxy.Calls);
            Assert.Equal("ReceiveMessage", method);
            return args[0]!.GetType().GetProperty("content")!.GetValue(args[0]) as string;
        }

        [Fact]
        public async Task Create_BadName_IsBlocked_AndKeepsInput()
        {
            var controller = Controller();

            var result = await controller.Create("Zorbak squad", "Study Group", "notes", 8);

            Assert.IsType<ViewResult>(result);
            Assert.Equal(BlockedCopy, controller.TempData["Error"]);
            Assert.Equal("Zorbak squad", controller.ViewData["Name"]);
            Assert.Equal("Study Group", controller.ViewData["Category"]);
            Assert.Equal("notes", controller.ViewData["Description"]);
            Assert.Equal(8, controller.ViewData["MaxMembers"]);
            Assert.Empty(_db.NewContext().Groups);
        }

        [Fact]
        public async Task Create_BadDescription_IsBlocked()
        {
            var controller = Controller();

            var result = await controller.Create("Thesis team", "Study Group", "we z.o.r.b.a.k here", 8);

            Assert.IsType<ViewResult>(result);
            Assert.Equal(BlockedCopy, controller.TempData["Error"]);
            Assert.Empty(_db.NewContext().Groups);
        }

        [Fact]
        public async Task Create_OtherErrors_AlsoKeepInput()
        {
            var controller = Controller();

            await controller.Create("Thesis team", "Not a category", "notes", 8);

            Assert.Equal("Thesis team", controller.ViewData["Name"]);
            Assert.Equal(8, controller.ViewData["MaxMembers"]);
        }

        [Fact]
        public async Task Create_NullDescription_CleanName_Works()
        {
            var result = await Controller().Create("Thesis team", "Study Group", null, 8);

            var redirect = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("Details", redirect.ActionName);
            Assert.Single(_db.NewContext().Groups);
        }

        [Fact]
        public async Task SendMessage_MasksBeforeSaveAndBroadcast()
        {
            var group = AddGroupWithMember();
            var (hub, clients) = Hub();

            await hub.SendMessage(group.GroupID, "ang zorbak mo");

            Assert.Equal("ang ****** mo", _db.NewContext().GroupMessages.Single().Content);
            Assert.Equal("ang ****** mo", BroadcastContent(clients));
        }

        [Fact]
        public async Task SendMessage_AllBadWords_SendsStars()
        {
            var group = AddGroupWithMember();
            var (hub, clients) = Hub();

            await hub.SendMessage(group.GroupID, "zorbak flimbo");

            Assert.Equal("****** ******", _db.NewContext().GroupMessages.Single().Content);
            Assert.Equal("****** ******", BroadcastContent(clients));
        }

        [Fact]
        public async Task SendMessage_CleanText_Unchanged()
        {
            var group = AddGroupWithMember();
            var (hub, _) = Hub();

            await hub.SendMessage(group.GroupID, "see you at 3pm!");

            Assert.Equal("see you at 3pm!", _db.NewContext().GroupMessages.Single().Content);
        }
    }
}
