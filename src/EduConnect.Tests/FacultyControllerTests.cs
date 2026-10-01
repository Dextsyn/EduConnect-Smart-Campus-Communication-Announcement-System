using EduConnect.Web;
using EduConnect.Web.Controllers;
using EduConnect.Web.Models;
using Microsoft.AspNetCore.Http;
using EduConnect.Web.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace EduConnect.Tests
{
    public class FacultyControllerTests : IDisposable
    {
        private readonly TestDb _db = new();

        public void Dispose() => _db.Dispose();

        private Announcement Post(User author, string title, string status, string approvalStatus)
        {
            var a = _db.AddAnnouncement(author, title);
            a.Status = status;
            a.ApprovalStatus = approvalStatus;
            _db.Context.SaveChanges();
            return a;
        }

        [Fact]
        public async Task Index_MySubmissions_ListsOnlyWhatStillNeedsAction()
        {
            var faculty = _db.AddUser(RoleNames.Faculty);
            var colleague = _db.AddUser(RoleNames.Faculty);
            Post(faculty, "Waiting on chair", "Draft", "PendingChair");
            Post(faculty, "Waiting on dean", "Draft", "PendingDean");
            var rejected = Post(faculty, "Sent back", "Draft", "Rejected");
            rejected.ChairRejectionReason = "Wrong date";
            Post(faculty, "Ready to publish", "Draft", "Approved");
            Post(faculty, "Unsubmitted draft", "Draft", "Draft");
            Post(faculty, "Live", "Published", "Approved");
            Post(faculty, "Withdrawn", "Archived", "PendingChair");
            Post(colleague, "Someone else's", "Draft", "PendingChair");
            _db.Context.SaveChanges();

            var session = new FakeSession();
            session.SetString("UserID", faculty.UserID.ToString());
            session.SetString("RoleName", RoleNames.Faculty);
            var controller = new FacultyController(_db.Context, NullLogger<FacultyController>.Instance)
            {
                ControllerContext = new ControllerContext { HttpContext = FakeSession.HttpContextWith(session) }
            };

            await controller.Index();
            var rows = (List<FacultySubmissionRow>)controller.ViewBag.MySubmissions;

            Assert.Equal(
                new[] { "Ready to publish", "Sent back", "Waiting on chair", "Waiting on dean" },
                rows.Select(r => r.Title).OrderBy(t => t));
            Assert.Equal("Wrong date", rows.Single(r => r.Title == "Sent back").RejectionReason);
        }
    }
}
