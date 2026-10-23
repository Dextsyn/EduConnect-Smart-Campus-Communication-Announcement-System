using EduConnect.Web;
using EduConnect.Web.Controllers;
using EduConnect.Web.Models;
using EduConnect.Web.Services;
using EduConnect.Web.ViewModels;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace EduConnect.Tests
{
    // An event is for its organizer's college, or only their department;
    // nobody else sees it (EventAudience).
    public class EventAudienceTests : IDisposable
    {
        private readonly TestDb _db = new();
        private readonly FakeSession _session = new();
        private readonly College _ccit, _coe;
        private readonly Department _itis, _cs, _ce;
        private readonly User _organizer;

        public EventAudienceTests()
        {
            _ccit = _db.AddCollege("CCIT");
            _itis = _db.AddDepartment(_ccit, "IT&IS");
            _cs = _db.AddDepartment(_ccit, "Computer Science");
            _coe = _db.AddCollege("Engineering");
            _ce = _db.AddDepartment(_coe, "Civil Engineering");
            _organizer = _db.AddUser(RoleNames.Faculty, _ccit.CollegeID, _itis.DepartmentID);
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

        private Event AddEvent(bool departmentOnly = false, User? organizer = null)
        {
            organizer ??= _organizer;
            var start = DateTime.Now.AddDays(3);
            var ev = new Event
            {
                OrganizerID = organizer.UserID,
                CollegeID = organizer.CollegeID,
                DepartmentID = organizer.DepartmentID,
                IsDepartmentOnly = departmentOnly,
                EventTitle = "Orientation",
                StartDateTime = start,
                EndDateTime = start.AddHours(2),
                IsRegistrationOpen = true,
                Status = "Upcoming"
            };
            _db.Context.Events.Add(ev);
            _db.Context.SaveChanges();
            return ev;
        }

        private User Student(Department dept) =>
            _db.AddUser(RoleNames.Student, dept.CollegeID, dept.DepartmentID);

        private async Task<List<int>> ListedFor(User user, string role)
        {
            var controller = Controller(user, role);
            Assert.IsType<ViewResult>(await controller.Index(null, null, null));
            return ((List<EventListViewModel>)controller.ViewBag.Events)
                .Select(e => e.EventID).ToList();
        }

        private async Task<bool> Visible(Event ev, User user, string role) =>
            await _db.NewContext().Events
                .Where(e => e.EventID == ev.EventID)
                .AnyAsync(EventAudience.VisibleTo(user.UserID, role, user.CollegeID, user.DepartmentID));

        // ─── College-wide ──────────────────────

        [Fact]
        public async Task CollegeEvent_StudentInOtherDepartmentOfSameCollege_SeesIt()
        {
            var ev = AddEvent();
            var student = Student(_cs);

            Assert.Contains(ev.EventID, await ListedFor(student, RoleNames.Student));
            Assert.IsType<ViewResult>(await Controller(student, RoleNames.Student).Details(ev.EventID));
        }

        [Fact]
        public async Task CollegeEvent_StudentInOtherCollege_HiddenAndRefused()
        {
            var ev = AddEvent();
            var student = Student(_ce);

            Assert.DoesNotContain(ev.EventID, await ListedFor(student, RoleNames.Student));
            Assert.IsType<NotFoundResult>(await Controller(student, RoleNames.Student).Details(ev.EventID));
            Assert.IsType<NotFoundResult>(await Controller(student, RoleNames.Student).Register(ev.EventID, consentGiven: true));
            Assert.Empty(_db.NewContext().EventRegistrations);
            Assert.Empty(_db.NewContext().EventWaitlist);
        }

        // ─── Department-only ───────────────────

        [Fact]
        public async Task DepartmentEvent_StudentInSameDepartment_SeesIt()
        {
            var ev = AddEvent(departmentOnly: true);
            Assert.True(await Visible(ev, Student(_itis), RoleNames.Student));
        }

        [Fact]
        public async Task DepartmentEvent_StudentInOtherDepartmentOfSameCollege_Hidden()
        {
            var ev = AddEvent(departmentOnly: true);
            var student = Student(_cs);

            Assert.DoesNotContain(ev.EventID, await ListedFor(student, RoleNames.Student));
            Assert.IsType<NotFoundResult>(await Controller(student, RoleNames.Student).Details(ev.EventID));
        }

        // ─── Other viewers ─────────────────────

        [Fact]
        public async Task FacultyOfOtherCollege_Hidden()
        {
            var ev = AddEvent();
            var faculty = _db.AddUser(RoleNames.Faculty, _coe.CollegeID, _ce.DepartmentID);
            Assert.False(await Visible(ev, faculty, RoleNames.Faculty));
        }

        [Theory]
        [InlineData(RoleNames.Administrator)]
        [InlineData(RoleNames.Staff)]
        [InlineData(RoleNames.StudentPending)]
        public async Task AdminStaffAndPending_SeeNoEvents(string role)
        {
            var ev = AddEvent();
            // Even a placement in the event's college doesn't count.
            var user = _db.AddUser(role, _ccit.CollegeID, _itis.DepartmentID);
            Assert.False(await Visible(ev, user, role));
        }

        [Fact]
        public async Task RegistrantFromOutside_KeepsAccess()
        {
            var ev = AddEvent();
            var outsider = Student(_ce);
            _db.Context.EventRegistrations.Add(new EventRegistration
            {
                EventID = ev.EventID, UserID = outsider.UserID,
                Status = "Registered", RegisteredAt = DateTime.Now
            });
            _db.Context.SaveChanges();

            Assert.IsType<ViewResult>(await Controller(outsider, RoleNames.Student).Details(ev.EventID));
        }

        [Fact]
        public async Task WaitlistedFromOutside_KeepsAccess()
        {
            var ev = AddEvent();
            var outsider = Student(_ce);
            _db.Context.EventWaitlist.Add(new EventWaitlist
            {
                EventID = ev.EventID, UserID = outsider.UserID,
                Position = 1, Status = "Waiting", JoinedAt = DateTime.Now
            });
            _db.Context.SaveChanges();

            Assert.True(await Visible(ev, outsider, RoleNames.Student));
        }

        [Fact]
        public async Task OrganizerAndManagers_SeeDepartmentEvent()
        {
            var ev = AddEvent(departmentOnly: true);
            var dean = _db.AddUser(RoleNames.Dean, _ccit.CollegeID);
            var chair = _db.AddUser(RoleNames.Chairperson, _ccit.CollegeID, _itis.DepartmentID);
            var otherChair = _db.AddUser(RoleNames.Chairperson, _ccit.CollegeID, _cs.DepartmentID);

            Assert.True(await Visible(ev, _organizer, RoleNames.Faculty));
            Assert.True(await Visible(ev, dean, RoleNames.Dean));
            Assert.True(await Visible(ev, chair, RoleNames.Chairperson));
            Assert.False(await Visible(ev, otherChair, RoleNames.Chairperson));
        }

        [Fact]
        public async Task EventWithoutCollege_OnlyOrganizerSeesIt()
        {
            var unplaced = _db.AddUser(RoleNames.Faculty);
            var ev = AddEvent(organizer: unplaced);

            Assert.True(await Visible(ev, unplaced, RoleNames.Faculty));
            Assert.False(await Visible(ev, Student(_itis), RoleNames.Student));
        }

        // ─── Origin is fixed ───────────────────

        [Fact]
        public async Task OrganizerMovesCollege_EventStaysWithOrigin()
        {
            var ev = AddEvent();
            var ccitStudent = Student(_cs);
            var coeStudent = Student(_ce);

            _organizer.CollegeID = _coe.CollegeID;
            _organizer.DepartmentID = _ce.DepartmentID;
            _db.Context.SaveChanges();

            Assert.True(await Visible(ev, ccitStudent, RoleNames.Student));
            Assert.False(await Visible(ev, coeStudent, RoleNames.Student));
        }

        // ─── Create ────────────────────────────

        private static EventFormViewModel Form(bool departmentOnly) => new()
        {
            EventTitle = "Orientation",
            StartDateTime = DateTime.Now.AddDays(3),
            EndDateTime = DateTime.Now.AddDays(3).AddHours(2),
            DepartmentOnly = departmentOnly
        };

        [Fact]
        public async Task Create_StampsOrganizerPlacement()
        {
            await Controller(_organizer, RoleNames.Faculty).Create(Form(departmentOnly: true));

            var ev = _db.NewContext().Events.Single();
            Assert.Equal(_ccit.CollegeID, ev.CollegeID);
            Assert.Equal(_itis.DepartmentID, ev.DepartmentID);
            Assert.True(ev.IsDepartmentOnly);
        }

        [Fact]
        public async Task Create_DeanEvent_IsCollegeWide()
        {
            var dean = _db.AddUser(RoleNames.Dean, _ccit.CollegeID);

            await Controller(dean, RoleNames.Dean).Create(Form(departmentOnly: true));

            var ev = _db.NewContext().Events.Single();
            Assert.Equal(_ccit.CollegeID, ev.CollegeID);
            Assert.Null(ev.DepartmentID);
            Assert.False(ev.IsDepartmentOnly);
        }

        [Fact]
        public async Task Create_ImplicitDepartment_IsCollegeWide()
        {
            var law = _db.AddCollege("College of Law", flat: true);
            var implicitDept = _db.NewContext().Departments.Single(d => d.CollegeID == law.CollegeID);
            var faculty = _db.AddUser(RoleNames.Faculty, law.CollegeID, implicitDept.DepartmentID);

            await Controller(faculty, RoleNames.Faculty).Create(Form(departmentOnly: true));

            Assert.False(_db.NewContext().Events.Single().IsDepartmentOnly);
        }

        [Fact]
        public async Task Create_OrganizerWithoutCollege_Refused()
        {
            var unplaced = _db.AddUser(RoleNames.Faculty);
            var controller = Controller(unplaced, RoleNames.Faculty);

            var result = await controller.Create(Form(departmentOnly: false));

            Assert.IsType<ViewResult>(result);
            Assert.Empty(_db.NewContext().Events);
        }

        [Fact]
        public async Task Edit_CanNarrowToDepartment_OriginUnchanged()
        {
            var ev = AddEvent();
            var form = Form(departmentOnly: true);
            form.EventID = ev.EventID;

            await Controller(_organizer, RoleNames.Faculty).Edit(ev.EventID, form);

            var saved = _db.NewContext().Events.Single();
            Assert.True(saved.IsDepartmentOnly);
            Assert.Equal(_ccit.CollegeID, saved.CollegeID);
            Assert.Equal(_itis.DepartmentID, saved.DepartmentID);
        }
    }
}
