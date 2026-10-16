using System.Reflection;
using EduConnect.Web;
using EduConnect.Web.Controllers;
using EduConnect.Web.Data;
using EduConnect.Web.Models;
using EduConnect.Web.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace EduConnect.Tests
{
    // Group Finder chats expire one week after the group is created, Open or
    // Full; joins and leaves never move that, and the hourly sweep dissolves
    // whatever is past it.
    public class GroupExpiryTests : IDisposable
    {
        private readonly TestDb _db = new();
        private readonly FakeSession _session = new();
        private readonly User _creator, _a, _b;

        public GroupExpiryTests()
        {
            _creator = _db.AddUser(RoleNames.Student);
            _a = _db.AddUser(RoleNames.Student);
            _b = _db.AddUser(RoleNames.Student);
        }

        public void Dispose() => _db.Dispose();

        private GroupController As(User user)
        {
            _session.SetString("UserID", user.UserID.ToString());
            _session.SetString("RoleName", RoleNames.Student);
            var http = FakeSession.HttpContextWith(_session);
            return new GroupController(_db.Context)
            {
                ControllerContext = new ControllerContext { HttpContext = http },
                TempData = new TempDataDictionary(http, new NullTempDataProvider())
            };
        }

        private Group AddGroup(int maxMembers, DateTime createdAt, string status = "Open",
            DateTime? expiresAt = null, params User[] members)
        {
            var group = new Group
            {
                CreatorID = _creator.UserID,
                Name = "Thesis team",
                Category = "Study Group",
                MaxMembers = maxMembers,
                Status = status,
                CreatedAt = createdAt,
                ChatExpiresAt = expiresAt
            };
            _db.Context.Groups.Add(group);
            _db.Context.SaveChanges();
            foreach (var u in members.Prepend(_creator))
                _db.Context.GroupMembers.Add(new GroupMember { GroupID = group.GroupID, UserID = u.UserID });
            _db.Context.GroupMessages.Add(new GroupMessage
            {
                GroupID = group.GroupID, SenderID = _creator.UserID, Content = "hi", SentAt = createdAt
            });
            _db.Context.SaveChanges();
            return group;
        }

        // Runs one pass of the hourly sweep (ExpireChats is private).
        private async Task Sweep()
        {
            var services = new ServiceCollection()
                .AddScoped<ApplicationDbContext>(_ => _db.NewContext())
                .BuildServiceProvider();
            var sweeper = new ChatExpiryService(services, NullLogger<ChatExpiryService>.Instance);
            var method = typeof(ChatExpiryService).GetMethod("ExpireChats",
                BindingFlags.Instance | BindingFlags.NonPublic)!;
            await (Task)method.Invoke(sweeper, null)!;
        }

        private Group Reload(Group g) => _db.NewContext().Groups.Single(x => x.GroupID == g.GroupID);
        private int MessageCount(Group g) => _db.NewContext().GroupMessages.Count(m => m.GroupID == g.GroupID);

        [Fact]
        public async Task Full_group_past_its_expiry_is_dissolved_by_the_sweep()
        {
            var g = AddGroup(2, DateTime.UtcNow.AddDays(-8), "Full",
                DateTime.UtcNow.AddMinutes(-1), _a);

            await Sweep();

            Assert.Equal("Dissolved", Reload(g).Status);
            Assert.Equal(0, MessageCount(g));
        }

        [Fact]
        public async Task Full_group_still_inside_its_week_is_left_alone()
        {
            var g = AddGroup(2, DateTime.UtcNow.AddDays(-3), "Full",
                DateTime.UtcNow.AddDays(4), _a);

            await Sweep();

            Assert.Equal("Full", Reload(g).Status);
            Assert.Equal(1, MessageCount(g));
        }

        [Fact]
        public async Task Creating_a_group_sets_its_deadline_a_week_out()
        {
            await As(_a).Create("Thesis team", "Study Group", null, 5);

            var saved = _db.NewContext().Groups.Single(g => g.CreatorID == _a.UserID);
            Assert.Equal("Open", saved.Status);
            Assert.Equal(saved.CreatedAt.AddDays(7), saved.ChatExpiresAt);
        }

        [Fact]
        public async Task Filling_a_group_keeps_its_creation_deadline()
        {
            var deadline = DateTime.UtcNow.AddDays(2);
            var g = AddGroup(2, DateTime.UtcNow.AddDays(-5), "Open", deadline);

            await As(_a).Join(g.GroupID);

            var saved = Reload(g);
            Assert.Equal("Full", saved.Status);
            Assert.Equal(deadline, saved.ChatExpiresAt);
        }

        [Fact]
        public async Task Leave_and_rejoin_never_moves_the_deadline()
        {
            var deadline = DateTime.UtcNow.AddHours(1);
            var g = AddGroup(2, DateTime.UtcNow.AddDays(-7).AddHours(1), "Full", deadline, _a);

            await As(_a).Leave(g.GroupID);
            Assert.Equal("Open", Reload(g).Status);
            Assert.Equal(deadline, Reload(g).ChatExpiresAt);

            await As(_b).Join(g.GroupID);
            Assert.Equal("Full", Reload(g).Status);
            Assert.Equal(deadline, Reload(g).ChatExpiresAt);
        }

        [Fact]
        public async Task Open_group_past_its_week_is_dissolved_by_the_sweep()
        {
            var g = AddGroup(5, DateTime.UtcNow.AddDays(-8), "Open",
                DateTime.UtcNow.AddDays(-1), _a);

            await Sweep();

            Assert.Equal("Dissolved", Reload(g).Status);
            Assert.Equal(0, MessageCount(g));
        }

        [Fact]
        public async Task Expired_group_awaiting_the_sweep_is_hidden_and_closed()
        {
            var g = AddGroup(5, DateTime.UtcNow.AddDays(-8), "Open", DateTime.UtcNow.AddMinutes(-5));

            var index = Assert.IsType<ViewResult>(await As(_a).Index());
            Assert.Empty(Assert.IsAssignableFrom<IEnumerable<Group>>(index.Model));

            await As(_a).Join(g.GroupID);
            Assert.Equal(1, _db.NewContext().GroupMembers.Count(m => m.GroupID == g.GroupID));

            // Nor does it hold the creator's one-group slot.
            await As(_creator).Create("Next team", "Study Group", null, 5);
            Assert.Equal(2, _db.NewContext().Groups.Count(x => x.CreatorID == _creator.UserID));
        }
    }
}
