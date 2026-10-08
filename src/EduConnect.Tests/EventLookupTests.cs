using System.Text.Json;
using EduConnect.Web;
using EduConnect.Web.Controllers;
using EduConnect.Web.Models;
using EduConnect.Web.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Logging.Abstractions;

namespace EduConnect.Tests
{
    public class EventLookupTests : IDisposable
    {
        private readonly TestDb _db = new();
        private readonly FakeSession _session = new();
        private readonly User _organizer, _otherFaculty;
        private readonly Event _event, _otherEvent;
        private readonly AcademicProgram _bsit;

        public EventLookupTests()
        {
            var ccit = _db.AddCollege("CCIT");
            var itis = _db.AddDepartment(ccit, "IT&IS");
            _bsit = _db.AddProgram(itis, "BSIT");
            _organizer = _db.AddUser(RoleNames.Faculty, ccit.CollegeID, itis.DepartmentID);
            _otherFaculty = _db.AddUser(RoleNames.Faculty);
            _event = AddEvent(_organizer, "Orientation");
            _otherEvent = AddEvent(_otherFaculty, "Other event");
        }

        public void Dispose() => _db.Dispose();

        private Event AddEvent(User organizer, string title, int daysFromNow = 0)
        {
            var ev = new Event
            {
                OrganizerID = organizer.UserID,
                EventTitle = title,
                StartDateTime = DateTime.Today.AddDays(daysFromNow).AddHours(23),
                EndDateTime = DateTime.Today.AddDays(daysFromNow).AddHours(23).AddMinutes(30)
            };
            _db.Context.Events.Add(ev);
            _db.Context.SaveChanges();
            return ev;
        }

        private EventRegistration Register(Event ev, string first, string last,
            string? studentId = null, string status = "Registered")
        {
            var student = _db.AddUser(RoleNames.Student, programId: _bsit.ProgramID);
            student.FirstName = first;
            student.LastName = last;
            student.StudentID = studentId;
            var registration = new EventRegistration { EventID = ev.EventID, UserID = student.UserID, Status = status };
            _db.Context.EventRegistrations.Add(registration);
            _db.Context.SaveChanges();
            return registration;
        }

        private EventController Controller(User as_, string role)
        {
            _session.SetString("UserID", as_.UserID.ToString());
            _session.SetString("RoleName", role);
            var controller = new EventController(
                _db.Context, NullLogger<EventController>.Instance, null!,
                new FakeEmailService(), new FakeNotificationService(), null!,
                new FakeBlobStorage(), new PlacementService(_db.Context));
            controller.ControllerContext = new ControllerContext { HttpContext = FakeSession.HttpContextWith(_session) };
            controller.TempData = new TempDataDictionary(controller.ControllerContext.HttpContext, new NullTempDataProvider());
            return controller;
        }

        private static JsonElement Json(IActionResult result) =>
            JsonSerializer.SerializeToElement(Assert.IsType<JsonResult>(result).Value);

        private async Task<string[]> Names(string q, Event? ev = null)
        {
            var json = Json(await Controller(_organizer, RoleNames.Faculty)
                .SearchRegistrants((ev ?? _event).EventID, q));
            return json.GetProperty("results").EnumerateArray()
                .Select(r => r.GetProperty("name").GetString()!).ToArray();
        }

        [Fact]
        public async Task Search_MatchesPartOfAName_IgnoringCaseAndAccents()
        {
            Register(_event, "Juan", "Peña");
            Register(_event, "Maria", "Santos");

            Assert.Equal(new[] { "Juan Peña" }, await Names("pen"));
            Assert.Equal(new[] { "Juan Peña" }, await Names("JUAN"));
        }

        [Fact]
        public async Task Search_EveryWordMustMatch_InAnyOrder()
        {
            Register(_event, "Juan", "Dela Cruz");
            Register(_event, "Juan", "Santos");

            Assert.Equal(new[] { "Juan Dela Cruz" }, await Names("dela cruz juan"));
            Assert.Equal(new[] { "Juan Dela Cruz", "Juan Santos" }, await Names("juan"));
        }

        [Fact]
        public async Task Search_ByStudentNumberPrefix()
        {
            Register(_event, "Ana", "Reyes", "202312345");
            Register(_event, "Ben", "Cruz", "202399999");

            Assert.Equal(new[] { "Ana Reyes" }, await Names("2023123"));
        }

        [Fact]
        public async Task Search_ResultsCarryTheRegistrationAndWhatTellsNamesApart()
        {
            var a = Register(_event, "Juan", "Cruz", "202300001");
            Register(_event, "Juan", "Cruz", "202300002", status: "Attended");

            var results = Json(await Controller(_organizer, RoleNames.Faculty).SearchRegistrants(_event.EventID, "juan cruz"))
                .GetProperty("results").EnumerateArray().ToList();

            Assert.Equal(2, results.Count);
            var first = results.Single(r => r.GetProperty("studentID").GetString() == "202300001");
            Assert.Equal(a.RegistrationID, first.GetProperty("registrationID").GetInt32());
            Assert.Equal("BSIT", first.GetProperty("placement").GetString());
            Assert.Equal("Registered", first.GetProperty("status").GetString());
        }

        [Fact]
        public async Task Search_OnlyTheChosenEventsRegistrants()
        {
            Register(_otherEvent, "Juan", "Cruz");

            Assert.Empty(await Names("juan"));
        }

        [Fact]
        public async Task Search_NeedsTwoCharacters()
        {
            Register(_event, "Juan", "Cruz");

            Assert.Empty(await Names("j"));
            Assert.Empty(await Names("  "));
        }

        [Fact]
        public async Task Search_ReturnsAtMostTen()
        {
            for (var i = 0; i < 12; i++)
                Register(_event, "Juan", "Cruz");

            Assert.Equal(10, (await Names("juan")).Length);
        }

        [Fact]
        public async Task Search_AnotherOrganizersEvent_IsForbidden()
        {
            Register(_otherEvent, "Juan", "Cruz");

            var json = Json(await Controller(_organizer, RoleNames.Faculty).SearchRegistrants(_otherEvent.EventID, "juan"));

            Assert.Equal("forbidden", json.GetProperty("error").GetString());
        }

        [Fact]
        public async Task Scanner_ListsOnlyEventsTheUserCanCheckInFor()
        {
            AddEvent(_organizer, "Past", daysFromNow: -1);

            var result = Assert.IsType<ViewResult>(await Controller(_organizer, RoleNames.Faculty).Scanner());

            var titles = JsonSerializer.SerializeToElement((object)result.ViewData["Events"]!)
                .EnumerateArray().Select(e => e.GetProperty("EventTitle").GetString()).ToArray();
            Assert.Equal(new[] { "Orientation" }, titles);
        }
    }
}
