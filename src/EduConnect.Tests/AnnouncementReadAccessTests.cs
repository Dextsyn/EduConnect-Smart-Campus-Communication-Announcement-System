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
    // Students read only what is addressed to them: outside an
    // announcement's audience, Details and feedback act as if it doesn't
    // exist, and an event links it only for viewers who could open it.
    public class AnnouncementReadAccessTests : IDisposable
    {
        private readonly TestDb _db = new();
        private readonly FakeSession _session = new();
        private readonly College _ccit, _coe;
        private readonly Department _itis, _cs, _ce;
        private readonly AcademicProgram _bsit, _bsis, _bscs, _bsce;
        private readonly User _faculty, _chair, _dean, _admin;
        private readonly Announcement _forBsit, _forItis, _schoolWide;

        public AnnouncementReadAccessTests()
        {
            _ccit = _db.AddCollege("CCIT");
            _itis = _db.AddDepartment(_ccit, "IT&IS");
            _cs = _db.AddDepartment(_ccit, "CS");
            _bsit = _db.AddProgram(_itis, "BSIT");
            _bsis = _db.AddProgram(_itis, "BSIS");
            _bscs = _db.AddProgram(_cs, "BSCS");
            _coe = _db.AddCollege("Engineering");
            _ce = _db.AddDepartment(_coe, "Civil");
            _bsce = _db.AddProgram(_ce, "BSCE");

            _faculty = _db.AddUser(RoleNames.Faculty, _ccit.CollegeID, _itis.DepartmentID);
            _chair = _db.AddUser(RoleNames.Chairperson, _ccit.CollegeID, _itis.DepartmentID);
            _dean = _db.AddUser(RoleNames.Dean, _ccit.CollegeID);
            _admin = _db.AddUser(RoleNames.Administrator);

            _forBsit = _db.AddAnnouncement(_faculty, "For BSIT");
            _db.Target(_forBsit, p: _bsit);
            _forItis = _db.AddAnnouncement(_faculty, "For IT&IS");
            _db.Target(_forItis, d: _itis);
            _schoolWide = _db.AddAnnouncement(_dean, "For everyone");
            _db.TagAnnouncement(_schoolWide, _db.AddTag(AudienceService.SchoolWide));
        }

        public void Dispose() => _db.Dispose();

        private User Student(AcademicProgram p) =>
            _db.AddUser(RoleNames.Student, _db.Context.Departments.Find(p.DepartmentID)!.CollegeID,
                p.DepartmentID, p.ProgramID);

        private void SignIn(User user)
        {
            _session.Clear();
            _session.SetString("UserID", user.UserID.ToString());
            _session.SetString("RoleName", _db.Context.Roles.Single(r => r.RoleID == user.RoleID).RoleName);
        }

        private AnnouncementController Announcements(User user)
        {
            SignIn(user);
            var controller = new AnnouncementController(
                _db.Context,
                NullLogger<AnnouncementController>.Instance,
                null!,
                new FakeNotificationService(),
                new FakeEmailService(),
                new FakeBlobStorage(),
                new AudienceService(_db.Context),
                new ApprovalService(_db.Context));
            controller.ControllerContext = new ControllerContext
            {
                HttpContext = FakeSession.HttpContextWith(_session)
            };
            controller.TempData = new TempDataDictionary(
                controller.ControllerContext.HttpContext, new NullTempDataProvider());
            return controller;
        }

        private EventController Events(User user)
        {
            SignIn(user);
            var controller = new EventController(
                _db.Context, NullLogger<EventController>.Instance, null!,
                new FakeEmailService(), new FakeNotificationService(), null!,
                new FakeBlobStorage(), new PlacementService(_db.Context),
                new AudienceService(_db.Context));
            controller.ControllerContext = new ControllerContext { HttpContext = FakeSession.HttpContextWith(_session) };
            controller.TempData = new TempDataDictionary(controller.ControllerContext.HttpContext, new NullTempDataProvider());
            return controller;
        }

        private async Task<bool> CanOpen(Announcement a, User user) =>
            await Announcements(user).Details(a.AnnouncementID) is ViewResult;

        // ─── Details ───────────────────────────

        [Fact]
        public async Task Details_ProgramPost_OnlyThatProgramsStudents()
        {
            Assert.True(await CanOpen(_forBsit, Student(_bsit)));
            Assert.False(await CanOpen(_forBsit, Student(_bsis)));
            Assert.False(await CanOpen(_forBsit, Student(_bscs)));
            Assert.False(await CanOpen(_forBsit, Student(_bsce)));
        }

        [Fact]
        public async Task Details_DepartmentPost_OnlyThatDepartmentsStudents()
        {
            Assert.True(await CanOpen(_forItis, Student(_bsit)));
            Assert.True(await CanOpen(_forItis, Student(_bsis)));
            Assert.False(await CanOpen(_forItis, Student(_bscs)));
            Assert.False(await CanOpen(_forItis, Student(_bsce)));
        }

        [Fact]
        public async Task Details_SchoolWidePost_EveryCollege()
        {
            Assert.True(await CanOpen(_schoolWide, Student(_bscs)));
            Assert.True(await CanOpen(_schoolWide, Student(_bsce)));
        }

        [Fact]
        public async Task Details_AuthorReviewersAndAdmin_StillOpenIt()
        {
            Assert.True(await CanOpen(_forBsit, _faculty));
            Assert.True(await CanOpen(_forBsit, _chair));
            Assert.True(await CanOpen(_forBsit, _dean));
            Assert.True(await CanOpen(_forBsit, _admin));
        }

        [Fact]
        public async Task Details_Refused_DoesNotCountAView()
        {
            await Announcements(Student(_bsce)).Details(_forBsit.AnnouncementID);

            var saved = _db.NewContext().Announcements.Single(a => a.AnnouncementID == _forBsit.AnnouncementID);
            Assert.Equal(0, saved.ViewCount);
            Assert.Empty(_db.NewContext().UserAnnouncementInteractions);
        }

        // ─── Feedback ──────────────────────────

        [Fact]
        public async Task SubmitFeedback_OutsideAudience_RefusedAndNotSaved()
        {
            var result = await Announcements(Student(_bsce)).SubmitFeedback(
                new FeedbackFormViewModel { AnnouncementID = _forItis.AnnouncementID, Rating = 5 });

            Assert.IsType<NotFoundResult>(result);
            Assert.Empty(_db.NewContext().Feedbacks);
        }

        [Fact]
        public async Task SubmitFeedback_InsideAudience_Saved()
        {
            await Announcements(Student(_bsis)).SubmitFeedback(
                new FeedbackFormViewModel { AnnouncementID = _forItis.AnnouncementID, Rating = 5 });

            Assert.Single(_db.NewContext().Feedbacks);
        }

        // ─── Event's related announcement ──────

        private Event CollegeEventLinking(Announcement a)
        {
            var start = DateTime.Now.AddDays(3);
            var ev = new Event
            {
                OrganizerID = _faculty.UserID,
                CollegeID = _ccit.CollegeID,
                DepartmentID = _itis.DepartmentID,
                IsDepartmentOnly = false,
                EventTitle = "Orientation",
                StartDateTime = start,
                EndDateTime = start.AddHours(2),
                IsRegistrationOpen = true,
                Status = "Upcoming",
                AnnouncementID = a.AnnouncementID
            };
            _db.Context.Events.Add(ev);
            _db.Context.SaveChanges();
            return ev;
        }

        private async Task<EventDetailViewModel> EventPage(Event ev, User user)
        {
            var view = Assert.IsType<ViewResult>(await Events(user).Details(ev.EventID));
            return Assert.IsType<EventDetailViewModel>(view.Model);
        }

        [Fact]
        public async Task EventLink_ShownToTheAnnouncementsAudience()
        {
            var page = await EventPage(CollegeEventLinking(_forItis), Student(_bsit));

            Assert.Equal(_forItis.AnnouncementID, page.AnnouncementID);
            Assert.Equal("For IT&IS", page.AnnouncementTitle);
        }

        [Fact]
        public async Task EventLink_HiddenFromEventViewersOutsideTheAnnouncementsAudience()
        {
            var page = await EventPage(CollegeEventLinking(_forItis), Student(_bscs));

            Assert.Null(page.AnnouncementID);
            Assert.Null(page.AnnouncementTitle);
        }

        [Fact]
        public async Task EventLink_SchoolWideAnnouncement_ShownToEveryEventViewer()
        {
            var page = await EventPage(CollegeEventLinking(_schoolWide), Student(_bscs));

            Assert.Equal(_schoolWide.AnnouncementID, page.AnnouncementID);
        }
    }
}
