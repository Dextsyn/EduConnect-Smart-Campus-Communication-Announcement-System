using EduConnect.Web;
using EduConnect.Web.Models;
using EduConnect.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace EduConnect.Tests
{
    public class ApprovalServiceTests : IDisposable
    {
        private readonly TestDb _db = new();
        private readonly College _ccit, _law;
        private readonly Department _itis, _cs;
        private readonly User _faculty, _chair, _chair2, _otherChair, _dean;

        public ApprovalServiceTests()
        {
            _ccit = _db.AddCollege("CCIT");
            _itis = _db.AddDepartment(_ccit, "IT&IS");
            _cs = _db.AddDepartment(_ccit, "CS");
            _law = _db.AddCollege("College of Law", flat: true);
            _faculty = _db.AddUser(RoleNames.Faculty, _ccit.CollegeID, _itis.DepartmentID);
            _chair = _db.AddUser(RoleNames.Chairperson, _ccit.CollegeID, _itis.DepartmentID);
            _chair2 = _db.AddUser(RoleNames.Chairperson, _ccit.CollegeID, _itis.DepartmentID);
            _otherChair = _db.AddUser(RoleNames.Chairperson, _ccit.CollegeID, _cs.DepartmentID);
            _dean = _db.AddUser(RoleNames.Dean, _ccit.CollegeID);
        }

        public void Dispose() => _db.Dispose();

        private ApprovalService Service => new(_db.Context);
        private AudienceService Audience => new(_db.Context);

        private static int[] Ids(IEnumerable<User> users) => users.Select(u => u.UserID).OrderBy(x => x).ToArray();

        [Fact]
        public async Task Route_FacultyWithChairs_PendingChairToAllActiveChairs()
        {
            var r = await Service.RouteAsync(_faculty.UserID, deanOnly: false);

            Assert.True(r.Ok);
            Assert.Equal("PendingChair", r.Status);
            Assert.Equal(Ids(new[] { _chair, _chair2 }), Ids(r.Reviewers));
        }

        [Fact]
        public async Task Route_InactiveChairsIgnored_FallsBackToDean()
        {
            foreach (var c in new[] { _chair, _chair2 }) c.IsActive = false;
            _db.Context.SaveChanges();

            var r = await Service.RouteAsync(_faculty.UserID, deanOnly: false);

            Assert.Equal("PendingDean", r.Status);
            Assert.Equal(Ids(new[] { _dean }), Ids(r.Reviewers));
        }

        [Fact]
        public async Task Route_FlatCollegeFaculty_GoesToDean()
        {
            var lawDept = _db.Context.Departments.Single(d => d.CollegeID == _law.CollegeID);
            var lawFaculty = _db.AddUser(RoleNames.Faculty, _law.CollegeID, lawDept.DepartmentID);
            var lawDean = _db.AddUser(RoleNames.Dean, _law.CollegeID);

            var r = await Service.RouteAsync(lawFaculty.UserID, deanOnly: false);

            Assert.Equal("PendingDean", r.Status);
            Assert.Equal(Ids(new[] { lawDean }), Ids(r.Reviewers));
        }

        [Fact]
        public async Task Route_UnplacedAuthor_Fails()
        {
            var unplaced = _db.AddUser(RoleNames.Faculty);

            var r = await Service.RouteAsync(unplaced.UserID, deanOnly: false);

            Assert.False(r.Ok);
            Assert.Contains("administrator", r.Error);
        }

        [Fact]
        public async Task Route_NoChairAndNoDean_Fails()
        {
            var lawDept = _db.Context.Departments.Single(d => d.CollegeID == _law.CollegeID);
            var lawFaculty = _db.AddUser(RoleNames.Faculty, _law.CollegeID, lawDept.DepartmentID);

            Assert.False((await Service.RouteAsync(lawFaculty.UserID, deanOnly: false)).Ok);
        }

        [Fact]
        public async Task Route_DeanOnly_SkipsChairs()
        {
            var r = await Service.RouteAsync(_faculty.UserID, deanOnly: true);

            Assert.Equal("PendingDean", r.Status);
            Assert.Equal(Ids(new[] { _dean }), Ids(r.Reviewers));
        }

        [Fact]
        public async Task Route_DeanOnlyWithoutDean_Fails()
        {
            _dean.IsActive = false;
            _db.Context.SaveChanges();

            var r = await Service.RouteAsync(_chair.UserID, deanOnly: true);

            Assert.False(r.Ok);
            Assert.Contains("no Dean", r.Error);
        }

        private Announcement Pending(User author, string status)
        {
            var a = _db.AddAnnouncement(author, $"{status} by {author.LastName}");
            a.ApprovalStatus = status;
            a.Status = "Draft";
            _db.Context.SaveChanges();
            return a;
        }

        private async Task<int[]> Reviewable(User reviewer)
        {
            var viewer = await Audience.GetViewerAsync(reviewer.UserID);
            return await Service.ReviewableBy(viewer).Select(a => a.AnnouncementID).OrderBy(x => x).ToArrayAsync();
        }

        [Fact]
        public async Task ReviewableBy_Chair_OwnDepartmentsPendingChairOnly()
        {
            var mine = Pending(_faculty, "PendingChair");
            Pending(_faculty, "PendingDean");
            var csFaculty = _db.AddUser(RoleNames.Faculty, _ccit.CollegeID, _cs.DepartmentID);
            Pending(csFaculty, "PendingChair");

            Assert.Equal(new[] { mine.AnnouncementID }, await Reviewable(_chair));
        }

        [Fact]
        public async Task ReviewableBy_Dean_CollegesPendingDeanOnly()
        {
            var mine = Pending(_chair, "PendingDean");
            Pending(_faculty, "PendingChair");
            var lawDean = _db.AddUser(RoleNames.Dean, _law.CollegeID);

            Assert.Equal(new[] { mine.AnnouncementID }, await Reviewable(_dean));
            Assert.Empty(await Reviewable(lawDean));
        }

        [Fact]
        public async Task ReviewableBy_Faculty_Nothing()
        {
            Pending(_faculty, "PendingChair");

            Assert.Empty(await Reviewable(_faculty));
        }
    }
}
