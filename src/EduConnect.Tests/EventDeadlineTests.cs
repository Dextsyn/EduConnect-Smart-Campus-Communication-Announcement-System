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
    public class EventDeadlineTests : IDisposable
    {
        private readonly TestDb _db = new();
        private readonly FakeSession _session = new();
        private readonly User _organizer;

        public EventDeadlineTests()
        {
            _organizer = _db.AddUser(RoleNames.Faculty);
        }

        public void Dispose() => _db.Dispose();

        private EventController Controller(User user, string role)
        {
            _session.SetString("UserID", user.UserID.ToString());
            _session.SetString("RoleName", role);
            var controller = new EventController(
                _db.Context, NullLogger<EventController>.Instance, null!,
                new FakeEmailService(), new FakeNotificationService(), null!,
                new FakeBlobStorage(), new PlacementService(_db.Context));
            controller.ControllerContext = new ControllerContext { HttpContext = FakeSession.HttpContextWith(_session) };
            controller.TempData = new TempDataDictionary(controller.ControllerContext.HttpContext, new NullTempDataProvider());
            return controller;
        }

        private static DateTime Minute(DateTime d) =>
            new(d.Year, d.Month, d.Day, d.Hour, d.Minute, 0);

        private static readonly DateTime Start = Minute(DateTime.Now.AddDays(3));

        private static EventFormViewModel Form(DateTime? deadline, DateTime? start = null) => new()
        {
            EventTitle = "Orientation",
            StartDateTime = start ?? Start,
            EndDateTime = (start ?? Start).AddHours(2),
            RegistrationDeadline = deadline
        };

        private Event AddEvent(DateTime start, DateTime? deadline)
        {
            var ev = new Event
            {
                OrganizerID = _organizer.UserID,
                EventTitle = "Orientation",
                StartDateTime = start,
                EndDateTime = start.AddHours(2),
                RegistrationDeadline = deadline,
                IsRegistrationOpen = true,
                Status = "Upcoming"
            };
            _db.Context.Events.Add(ev);
            _db.Context.SaveChanges();
            return ev;
        }

        [Fact]
        public async Task Create_DeadlineEqualToStart_Rejected()
        {
            var controller = Controller(_organizer, RoleNames.Faculty);

            var result = await controller.Create(Form(Start));

            Assert.IsType<ViewResult>(result);
            Assert.True(controller.ModelState.ContainsKey("RegistrationDeadline"));
            Assert.Empty(_db.NewContext().Events);
        }

        [Fact]
        public async Task Create_DeadlineAfterStart_Rejected()
        {
            var controller = Controller(_organizer, RoleNames.Faculty);

            await controller.Create(Form(Start.AddHours(1)));

            Assert.True(controller.ModelState.ContainsKey("RegistrationDeadline"));
            Assert.Empty(_db.NewContext().Events);
        }

        [Fact]
        public async Task Create_DeadlineInThePast_Rejected()
        {
            var controller = Controller(_organizer, RoleNames.Faculty);

            await controller.Create(Form(Minute(DateTime.Now.AddHours(-1))));

            Assert.True(controller.ModelState.ContainsKey("RegistrationDeadline"));
            Assert.Empty(_db.NewContext().Events);
        }

        [Fact]
        public async Task Create_DeadlineBeforeStart_Saved()
        {
            var deadline = Start.AddDays(-1);

            var result = await Controller(_organizer, RoleNames.Faculty).Create(Form(deadline));

            Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal(deadline, _db.NewContext().Events.Single().RegistrationDeadline);
        }

        [Fact]
        public async Task Edit_DeadlineEqualToStart_Rejected()
        {
            var ev = AddEvent(Start, Start.AddDays(-1));
            var controller = Controller(_organizer, RoleNames.Faculty);

            await controller.Edit(ev.EventID, Form(Start));

            Assert.True(controller.ModelState.ContainsKey("RegistrationDeadline"));
            Assert.Equal(Start.AddDays(-1), _db.NewContext().Events.Single().RegistrationDeadline);
        }

        [Fact]
        public async Task Edit_KeepsAPassedDeadline()
        {
            // Registration already closed; the organizer only fixes the title.
            var passed = Minute(DateTime.Now.AddHours(-2));
            var ev = AddEvent(Start, passed);
            var form = Form(passed);
            form.EventTitle = "Orientation (Room 301)";
            var controller = Controller(_organizer, RoleNames.Faculty);

            await controller.Edit(ev.EventID, form);

            Assert.False(controller.ModelState.ContainsKey("RegistrationDeadline"));
            Assert.Equal("Orientation (Room 301)", _db.NewContext().Events.Single().EventTitle);
        }

        [Fact]
        public async Task Register_AfterEventStarted_Refused()
        {
            // A deadline after the start used to let this through.
            var ev = AddEvent(DateTime.Now.AddHours(-1), DateTime.Now.AddHours(5));
            var student = _db.AddUser(RoleNames.Student);
            var controller = Controller(student, RoleNames.Student);

            await controller.Register(ev.EventID, consentGiven: true);

            Assert.Empty(_db.NewContext().EventRegistrations);
            Assert.Equal("Registration is closed.", controller.TempData["Error"]);
        }
    }
}
