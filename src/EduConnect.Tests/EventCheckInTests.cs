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
    public class EventCheckInTests : IDisposable
    {
        private readonly TestDb _db = new();
        private readonly FakeSession _session = new();
        private readonly User _organizer;
        private readonly EventRegistration _registration;

        public EventCheckInTests()
        {
            _organizer = _db.AddUser(RoleNames.Faculty);
            var student = _db.AddUser(RoleNames.Student);
            var ev = new Event
            {
                OrganizerID = _organizer.UserID,
                EventTitle = "Orientation",
                StartDateTime = DateTime.Now,
                EndDateTime = DateTime.Now.AddHours(2)
            };
            _db.Context.Events.Add(ev);
            _db.Context.SaveChanges();

            _registration = new EventRegistration { EventID = ev.EventID, UserID = student.UserID };
            _db.Context.EventRegistrations.Add(_registration);
            _db.Context.SaveChanges();
        }

        public void Dispose() => _db.Dispose();

        private EventController Scanner()
        {
            _session.SetString("UserID", _organizer.UserID.ToString());
            _session.SetString("RoleName", RoleNames.Faculty);
            var controller = new EventController(
                _db.Context, NullLogger<EventController>.Instance, null!,
                new FakeEmailService(), new FakeNotificationService(), null!,
                new FakeBlobStorage(), new PlacementService(_db.Context));
            controller.ControllerContext = new ControllerContext { HttpContext = FakeSession.HttpContextWith(_session) };
            controller.TempData = new TempDataDictionary(controller.ControllerContext.HttpContext, new NullTempDataProvider());
            return controller;
        }

        private EventRegistration Saved() =>
            _db.NewContext().EventRegistrations.Single(r => r.RegistrationID == _registration.RegistrationID);

        [Fact]
        public async Task Scan_RecordsCheckInTime()
        {
            var before = DateTime.Now;

            await Scanner().MarkAttendanceAjax(_registration.RegistrationID);

            var saved = Saved();
            Assert.Equal("Attended", saved.Status);
            Assert.NotNull(saved.CheckedInAt);
            Assert.InRange(saved.CheckedInAt!.Value, before, DateTime.Now);
        }

        [Fact]
        public async Task Scan_Again_KeepsTheFirstCheckInTime()
        {
            await Scanner().MarkAttendanceAjax(_registration.RegistrationID);
            var first = Saved().CheckedInAt;

            await Scanner().MarkAttendanceAjax(_registration.RegistrationID);

            Assert.Equal(first, Saved().CheckedInAt);
        }

        [Fact]
        public async Task Registrants_ShowsTheCheckInTime()
        {
            await Scanner().MarkAttendanceAjax(_registration.RegistrationID);

            var result = (ViewResult)await Scanner().Registrants(_registration.EventID);
            var row = Assert.Single(((EventRegistrantsViewModel)result.Model!).Registrations);

            Assert.Equal(Saved().CheckedInAt, row.CheckedInAt);
        }
    }
}
