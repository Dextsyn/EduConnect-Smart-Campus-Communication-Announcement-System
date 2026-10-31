using EduConnect.Web;
using EduConnect.Web.Controllers;
using EduConnect.Web.Hubs;
using EduConnect.Web.Models;
using EduConnect.Web.Services;
using EduConnect.Web.ViewModels;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;

namespace EduConnect.Tests
{
    // Registering again after cancelling, and a waitlisted student taking a
    // spot that opened up.
    public class EventRegistrationTests : IDisposable
    {
        private readonly TestDb _db = new();
        private readonly FakeSession _session = new();
        private readonly FakeNotificationService _notifications = new();
        private readonly College _college;
        private readonly Department _department;
        private readonly User _organizer;

        public EventRegistrationTests()
        {
            _college = _db.AddCollege("CCIT");
            _department = _db.AddDepartment(_college, "IT&IS");
            _organizer = _db.AddUser(RoleNames.Faculty,
                _college.CollegeID, _department.DepartmentID);
        }

        public void Dispose() => _db.Dispose();

        private EventController Controller(User user)
        {
            _session.SetString("UserID", user.UserID.ToString());
            _session.SetString("RoleName", RoleNames.Student);
            var controller = new EventController(
                _db.Context, NullLogger<EventController>.Instance, null!,
                new FakeEmailService(), _notifications, new FakeEventHub(),
                new FakeBlobStorage(), new PlacementService(_db.Context),
                new AudienceService(_db.Context));
            controller.ControllerContext = new ControllerContext { HttpContext = FakeSession.HttpContextWith(_session) };
            controller.TempData = new TempDataDictionary(controller.ControllerContext.HttpContext, new NullTempDataProvider());
            return controller;
        }

        private Event AddEvent(int? maxAttendees = null)
        {
            var start = DateTime.Now.AddDays(3);
            var ev = new Event
            {
                OrganizerID = _organizer.UserID,
                CollegeID = _college.CollegeID,
                DepartmentID = _department.DepartmentID,
                EventTitle = "Orientation",
                StartDateTime = start,
                EndDateTime = start.AddHours(2),
                MaxAttendees = maxAttendees,
                IsRegistrationOpen = true,
                Status = "Upcoming"
            };
            _db.Context.Events.Add(ev);
            _db.Context.SaveChanges();
            return ev;
        }

        private User Student() => _db.AddUser(RoleNames.Student, _college.CollegeID);

        private void AddRegistration(Event ev, User user, string status)
        {
            _db.Context.EventRegistrations.Add(new EventRegistration
            {
                EventID = ev.EventID, UserID = user.UserID, Status = status
            });
            _db.Context.SaveChanges();
        }

        private void AddWaitlist(Event ev, User user, int position, string status)
        {
            _db.Context.EventWaitlist.Add(new EventWaitlist
            {
                EventID = ev.EventID, UserID = user.UserID,
                Position = position, Status = status
            });
            _db.Context.SaveChanges();
        }

        [Fact]
        public async Task Register_AfterCancelling_RegistersAgain()
        {
            var ev = AddEvent();
            var student = Student();
            AddRegistration(ev, student, "Cancelled");

            await Controller(student).Register(ev.EventID, consentGiven: true);

            var registration = _db.NewContext().EventRegistrations.Single();
            Assert.Equal("Registered", registration.Status);
            Assert.NotNull(registration.QRCode);
        }

        [Fact]
        public async Task Details_AfterCancelling_OffersRegistrationAgain()
        {
            var ev = AddEvent();
            var student = Student();
            AddRegistration(ev, student, "Cancelled");

            var result = await Controller(student).Details(ev.EventID);

            var model = Assert.IsType<EventDetailViewModel>(
                Assert.IsType<ViewResult>(result).Model);
            Assert.False(model.IsRegistered);
            Assert.Equal("Open", model.RegistrationStatus);
        }

        [Fact]
        public async Task CancelRegistration_LeavesTheSpotOpenWithoutATimeLimit()
        {
            var ev = AddEvent(maxAttendees: 1);
            var leaving = Student();
            var waiting = Student();
            AddRegistration(ev, leaving, "Registered");
            AddWaitlist(ev, waiting, 1, "Waiting");

            await Controller(leaving).CancelRegistration(ev.EventID);

            Assert.Equal("Notified", _db.NewContext().EventWaitlist.Single().Status);
            Assert.Contains((waiting.UserID, "WaitlistPromotion"), _notifications.Sent);
        }

        [Fact]
        public async Task Register_NotifiedWaitlister_TakesTheOpenSpot()
        {
            var ev = AddEvent(maxAttendees: 1);
            var waiting = Student();
            AddRegistration(ev, Student(), "Cancelled");
            AddWaitlist(ev, waiting, 1, "Notified");

            var controller = Controller(waiting);
            var details = Assert.IsType<EventDetailViewModel>(
                Assert.IsType<ViewResult>(await controller.Details(ev.EventID)).Model);
            Assert.Equal("Open", details.RegistrationStatus);

            await controller.Register(ev.EventID, consentGiven: true);

            var db = _db.NewContext();
            Assert.Equal("Registered",
                db.EventRegistrations.Single(r => r.UserID == waiting.UserID).Status);
            Assert.Empty(db.EventWaitlist);
        }

        [Fact]
        public async Task Register_WaitlisterWhenSpotWasTaken_StaysOnWaitlist()
        {
            var ev = AddEvent(maxAttendees: 1);
            var waiting = Student();
            AddRegistration(ev, Student(), "Registered");
            AddWaitlist(ev, waiting, 1, "Notified");

            var controller = Controller(waiting);
            await controller.Register(ev.EventID, consentGiven: true);

            var db = _db.NewContext();
            Assert.DoesNotContain(db.EventRegistrations, r => r.UserID == waiting.UserID);
            Assert.Equal("Waiting", db.EventWaitlist.Single().Status);
        }

        [Fact]
        public async Task Details_WaitlistPosition_CountsOnlyPeopleStillWaiting()
        {
            // #1 already took a spot and left the waitlist; #2 is now first.
            var ev = AddEvent(maxAttendees: 1);
            var second = Student();
            AddRegistration(ev, Student(), "Registered");
            AddWaitlist(ev, second, 2, "Waiting");

            var model = Assert.IsType<EventDetailViewModel>(
                Assert.IsType<ViewResult>(await Controller(second).Details(ev.EventID)).Model);

            Assert.True(model.IsOnWaitlist);
            Assert.Equal(1, model.WaitlistPosition);
        }

        [Theory]
        [InlineData(2, 4, "Upcoming", "Upcoming")]
        [InlineData(-1, 1, "Upcoming", "Ongoing")]
        [InlineData(-4, -2, "Upcoming", "Completed")]
        [InlineData(-4, -2, "Cancelled", "Cancelled")]
        public void DisplayStatus_FollowsTheClock(int startHours, int endHours, string stored, string shown)
        {
            var ev = new Event
            {
                StartDateTime = DateTime.Now.AddHours(startHours),
                EndDateTime = DateTime.Now.AddHours(endHours),
                Status = stored
            };

            Assert.Equal(shown, ev.DisplayStatus);
        }

        private sealed class FakeEventHub : IHubContext<EventHub>
        {
            public IHubClients Clients { get; } = new NullClients();
            public IGroupManager Groups => throw new NotSupportedException();

            private sealed class NullClients : IHubClients
            {
                private sealed class NullProxy : IClientProxy
                {
                    public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
                        => Task.CompletedTask;
                }

                private static readonly IClientProxy Proxy = new NullProxy();
                public IClientProxy All => Proxy;
                public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => Proxy;
                public IClientProxy Client(string connectionId) => Proxy;
                public IClientProxy Clients(IReadOnlyList<string> connectionIds) => Proxy;
                public IClientProxy Group(string groupName) => Proxy;
                public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => Proxy;
                public IClientProxy Groups(IReadOnlyList<string> groupNames) => Proxy;
                public IClientProxy User(string userId) => Proxy;
                public IClientProxy Users(IReadOnlyList<string> userIds) => Proxy;
            }
        }
    }
}
