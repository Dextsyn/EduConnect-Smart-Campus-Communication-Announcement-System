using EduConnect.Web;
using EduConnect.Web.Controllers;
using EduConnect.Web.Services;
using EduConnect.Web.ViewModels;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace EduConnect.Tests
{
    public class DeanControllerTests : IDisposable
    {
        private readonly TestDb _db = new();

        public void Dispose() => _db.Dispose();

        [Fact]
        public async Task Index_AccountDeletedWhileLoggedIn_EndsTheSession()
        {
            var session = new FakeSession();
            session.SetString("UserID", "999");
            session.SetString("RoleName", RoleNames.Dean);
            var controller = new DeanController(_db.Context, NullLogger<DeanController>.Instance, new AudienceService(_db.Context), new ApprovalService(_db.Context))
            {
                ControllerContext = new ControllerContext { HttpContext = FakeSession.HttpContextWith(session) }
            };

            var result = await controller.Index();

            Assert.IsType<RedirectToActionResult>(result);
            Assert.Empty(session.Keys);
        }

        [Fact]
        public async Task Index_ListsWhatTheViewerCanApprove()
        {
            var ccit = _db.AddCollege("CCIT");
            var itis = _db.AddDepartment(ccit, "IT&IS");
            var faculty = _db.AddUser(RoleNames.Faculty, ccit.CollegeID, itis.DepartmentID);
            var chair = _db.AddUser(RoleNames.Chairperson, ccit.CollegeID, itis.DepartmentID);
            var dean = _db.AddUser(RoleNames.Dean, ccit.CollegeID);
            var forChair = _db.AddAnnouncement(faculty, "Needs the chair");
            forChair.Status = "Draft";
            forChair.ApprovalStatus = "PendingChair";
            forChair.SubmittedAt = DateTime.Now;
            _db.AddAnnouncement(faculty, "Already out");
            _db.Context.SaveChanges();

            var chairRows = await PendingFor(chair, RoleNames.Chairperson);
            var deanRows = await PendingFor(dean, RoleNames.Dean);

            var row = Assert.Single(chairRows);
            Assert.Equal("Needs the chair", row.Title);
            Assert.Empty(deanRows);
        }

        private async Task<List<PendingAnnouncementRow>> PendingFor(EduConnect.Web.Models.User user, string role)
        {
            var session = new FakeSession();
            session.SetString("UserID", user.UserID.ToString());
            session.SetString("RoleName", role);
            var controller = new DeanController(_db.Context, NullLogger<DeanController>.Instance, new AudienceService(_db.Context), new ApprovalService(_db.Context))
            {
                ControllerContext = new ControllerContext { HttpContext = FakeSession.HttpContextWith(session) }
            };

            await controller.Index();

            return (List<PendingAnnouncementRow>)controller.ViewBag.PendingAnnouncements;
        }
    }
}
