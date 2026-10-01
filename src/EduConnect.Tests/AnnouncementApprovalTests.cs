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
    public class AnnouncementApprovalTests : IDisposable
    {
        private readonly TestDb _db = new();
        private readonly FakeNotificationService _notes = new();
        private readonly FakeEmailService _mail = new();
        private readonly FakeSession _session = new();
        private readonly College _ccit;
        private readonly Department _itis, _cs;
        private readonly AcademicProgram _bsit;
        private readonly User _faculty, _chair, _otherChair, _dean;
        private readonly Announcement _post;

        public AnnouncementApprovalTests()
        {
            _ccit = _db.AddCollege("CCIT");
            _itis = _db.AddDepartment(_ccit, "IT&IS");
            _cs = _db.AddDepartment(_ccit, "CS");
            _bsit = _db.AddProgram(_itis, "BSIT");
            _faculty = _db.AddUser(RoleNames.Faculty, _ccit.CollegeID, _itis.DepartmentID);
            _chair = _db.AddUser(RoleNames.Chairperson, _ccit.CollegeID, _itis.DepartmentID);
            _otherChair = _db.AddUser(RoleNames.Chairperson, _ccit.CollegeID, _cs.DepartmentID);
            _dean = _db.AddUser(RoleNames.Dean, _ccit.CollegeID);

            _post = _db.AddAnnouncement(_faculty, "For BSIT");
            _post.Status = "Draft";
            _post.ApprovalStatus = "Draft";
            _db.Context.SaveChanges();
            _db.Target(_post, p: _bsit);
        }

        public void Dispose() => _db.Dispose();

        private AnnouncementController As(User user)
        {
            _session.Clear();
            _session.SetString("UserID", user.UserID.ToString());
            _session.SetString("RoleName", _db.Context.Roles.Single(r => r.RoleID == user.RoleID).RoleName);

            var controller = new AnnouncementController(
                _db.Context,
                NullLogger<AnnouncementController>.Instance,
                null!,
                _notes,
                _mail,
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

        private void SetStatus(string status)
        {
            _post.ApprovalStatus = status;
            _db.Context.SaveChanges();
        }

        private string StatusNow() =>
            _db.NewContext().Announcements.Single(a => a.AnnouncementID == _post.AnnouncementID).ApprovalStatus;

        [Fact]
        public async Task Submit_ProgramOnlyDraft_GoesToTheDepartmentsChair()
        {
            await As(_faculty).Submit(_post.AnnouncementID);

            Assert.Equal("PendingChair", StatusNow());
            Assert.Contains(_notes.Sent, n => n.UserId == _chair.UserID);
            Assert.DoesNotContain(_notes.Sent, n => n.UserId == _otherChair.UserID);
        }

        [Fact]
        public async Task Approve_Chair_IsFinalByDefault()
        {
            SetStatus("PendingChair");

            await As(_chair).Approve(_post.AnnouncementID);

            var saved = _db.NewContext().Announcements.Single(a => a.AnnouncementID == _post.AnnouncementID);
            Assert.Equal("Approved", saved.ApprovalStatus);
            Assert.Equal(_chair.UserID, saved.ApprovedByID);
            Assert.Contains(_notes.Sent, n => n.UserId == _faculty.UserID && n.Type == "AnnouncementApproved");
        }

        [Fact]
        public async Task Approve_ChairEscalate_ForwardsToDean()
        {
            SetStatus("PendingChair");

            await As(_chair).Approve(_post.AnnouncementID, escalateToDean: true);

            Assert.Equal("PendingDean", StatusNow());
            Assert.Contains(_notes.Sent, n => n.UserId == _dean.UserID);
        }

        [Fact]
        public async Task Approve_ChairOfOtherDepartment_CannotAct()
        {
            SetStatus("PendingChair");

            await As(_otherChair).Approve(_post.AnnouncementID);

            Assert.Equal("PendingChair", StatusNow());
        }

        [Fact]
        public async Task Approve_ChairEscalateWithoutDean_StaysPending()
        {
            SetStatus("PendingChair");
            _dean.IsActive = false;
            _db.Context.SaveChanges();

            var result = await As(_chair).Approve(_post.AnnouncementID, escalateToDean: true);

            Assert.Equal("PendingChair", StatusNow());
            Assert.Equal("Review", Assert.IsType<RedirectToActionResult>(result).ActionName);
        }

        private AnnouncementFormViewModel ChairPost() => new()
        {
            Title = "Dean-level notice",
            Body = "Body",
            CategoryID = _db.CategoryID(),
            Priority = 1,
            TargetDepartmentIDs = new List<int> { _itis.DepartmentID },
            RequiresDeanApproval = true
        };

        [Fact]
        public async Task Create_ChairRequiringDean_SavesPendingDeanAndNotifiesDean()
        {
            await As(_chair).Create(ChairPost());

            var saved = _db.NewContext().Announcements.Single(a => a.Title == "Dean-level notice");
            Assert.Equal("PendingDean", saved.ApprovalStatus);
            Assert.Equal("Draft", saved.Status);
            Assert.Contains(_notes.Sent, n => n.UserId == _dean.UserID);
        }

        [Fact]
        public async Task Create_ChairRequiringDeanWithoutDean_IsRefused()
        {
            _dean.IsActive = false;
            _db.Context.SaveChanges();

            var result = await As(_chair).Create(ChairPost());

            Assert.IsType<ViewResult>(result);
            Assert.False(_db.NewContext().Announcements.Any(a => a.Title == "Dean-level notice"));
        }

        [Fact]
        public async Task Reject_Dean_RecordsTheReason()
        {
            SetStatus("PendingDean");

            await As(_dean).Reject(_post.AnnouncementID, "Not this week");

            var saved = _db.NewContext().Announcements.Single(a => a.AnnouncementID == _post.AnnouncementID);
            Assert.Equal("Rejected", saved.ApprovalStatus);
            Assert.Equal("Not this week", saved.RejectionReason);
        }

        private Announcement ChairPostRejectedByDean()
        {
            var post = _db.AddAnnouncement(_chair, "Chair notice");
            post.Status = "Draft";
            post.ApprovalStatus = "Rejected";
            post.RejectionReason = "Not this week";
            _db.Context.SaveChanges();
            _db.Target(post, d: _itis);
            return post;
        }

        [Fact]
        public async Task Edit_ChairOwnPostRejectedByDean_ReturnsToDraft()
        {
            var post = ChairPostRejectedByDean();
            var form = ChairPost();
            form.AnnouncementID = post.AnnouncementID;

            await As(_chair).Edit(form);

            var saved = _db.NewContext().Announcements.Single(a => a.AnnouncementID == post.AnnouncementID);
            Assert.Equal("Draft", saved.ApprovalStatus);
            Assert.Null(saved.RejectionReason);
        }

        [Fact]
        public async Task Submit_ChairOwnPost_GoesToTheDean()
        {
            var post = ChairPostRejectedByDean();

            await As(_chair).Submit(post.AnnouncementID);

            Assert.Equal("PendingDean",
                _db.NewContext().Announcements.Single(a => a.AnnouncementID == post.AnnouncementID).ApprovalStatus);
            Assert.Contains(_notes.Sent, n => n.UserId == _dean.UserID);
        }

        [Fact]
        public async Task Index_AuthorsOwnDraft_IsNotInTheFeed()
        {
            var draft = ChairPostRejectedByDean();
            draft.ApprovalStatus = "Approved";
            var live = _db.AddAnnouncement(_chair, "Chair live notice");
            live.Status = "Published";
            _db.Context.SaveChanges();
            _db.Target(live, d: _itis);

            var controller = As(_chair);
            await controller.Index(null, null, null);
            var titles = ((List<AnnouncementTableViewModel>)controller.ViewBag.Announcements)
                .Select(a => a.Title).ToList();
            Assert.Contains("Chair live notice", titles);
            Assert.DoesNotContain("Chair notice", titles);
        }

        [Fact]
        public async Task Approve_ArchivedPost_IsNotReviewable()
        {
            _post.Status = "Archived";
            SetStatus("PendingChair");

            await As(_chair).Approve(_post.AnnouncementID);

            Assert.Equal("PendingChair", StatusNow());
        }

        [Fact]
        public async Task Publish_ArchivedApprovedPost_StaysArchived()
        {
            _post.Status = "Archived";
            SetStatus("Approved");

            await As(_faculty).Publish(_post.AnnouncementID);

            Assert.Equal("Archived",
                _db.NewContext().Announcements.Single(a => a.AnnouncementID == _post.AnnouncementID).Status);
            Assert.Empty(_notes.Sent);
        }

        [Fact]
        public async Task Edit_ReviewerApprovedPost_IsLocked()
        {
            SetStatus("Approved");
            _post.ApprovedByID = _chair.UserID;
            _db.Context.SaveChanges();

            var get = await As(_faculty).Edit(_post.AnnouncementID);
            var post = await As(_faculty).Edit(new AnnouncementFormViewModel
            {
                AnnouncementID = _post.AnnouncementID,
                Title = "Changed after approval",
                Body = "Body",
                CategoryID = _db.CategoryID(),
                TargetProgramIDs = new() { _bsit.ProgramID }
            });

            Assert.Equal("MyAnnouncements", Assert.IsType<RedirectToActionResult>(get).ActionName);
            Assert.Equal("MyAnnouncements", Assert.IsType<RedirectToActionResult>(post).ActionName);
            Assert.Equal("For BSIT",
                _db.NewContext().Announcements.Single(a => a.AnnouncementID == _post.AnnouncementID).Title);
        }

        [Fact]
        public async Task Edit_DeansOwnApprovedDraft_StaysEditable()
        {
            var draft = _db.AddAnnouncement(_dean, "Dean draft");
            draft.Status = "Draft";
            draft.PublishedAt = null;
            _db.Context.SaveChanges();

            var result = await As(_dean).Edit(draft.AnnouncementID);

            Assert.IsType<ViewResult>(result);
        }
    }
}
