using EduConnect.Web;
using EduConnect.Web.Controllers;
using EduConnect.Web.Models;
using EduConnect.Web.ViewModels;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Logging.Abstractions;

namespace EduConnect.Tests
{
    public class StaffActivityTests : IDisposable
    {
        private readonly TestDb _db = new();
        private readonly FakeSession _session = new();

        public void Dispose() => _db.Dispose();

        private void LogIn(User user, string role)
        {
            _session.SetString("UserID", user.UserID.ToString());
            _session.SetString("RoleName", role);
        }

        private T Wire<T>(T controller) where T : Controller
        {
            controller.ControllerContext = new ControllerContext { HttpContext = FakeSession.HttpContextWith(_session) };
            controller.TempData = new TempDataDictionary(controller.ControllerContext.HttpContext, new NullTempDataProvider());
            return controller;
        }

        private StaffController Staff(User staff)
        {
            LogIn(staff, RoleNames.Staff);
            return Wire(new StaffController(_db.Context));
        }

        private SafetyReportController Reporting(User user)
        {
            LogIn(user, RoleNames.Student);
            return Wire(new SafetyReportController(
                _db.Context, null!, new FakeNotificationService(), new FakeEmailService(),
                NullLogger<SafetyReportController>.Instance, new FakeBlobStorage()));
        }

        private static IFormFile PhotoFile()
        {
            var bytes = new byte[] { 0x89, 0x50, 0x4E, 0x47 };
            return new FormFile(new MemoryStream(bytes), 0, bytes.Length, "Photo", "leak.png")
            {
                Headers = new HeaderDictionary(),
                ContentType = "image/png"
            };
        }

        private IncidentReport AddReport(string building = "SV", string status = "Pending")
        {
            var report = new IncidentReport { IncidentType = building, Description = "Broken light", Status = status };
            _db.Context.IncidentReports.Add(report);
            _db.Context.SaveChanges();
            return report;
        }

        private List<IncidentReportActivity> Activities(int reportId) =>
            _db.NewContext().IncidentReportActivities
                .Where(a => a.ReportID == reportId)
                .OrderBy(a => a.ActivityID)
                .ToList();

        [Fact]
        public async Task Submit_RecordsReceived()
        {
            var student = _db.AddUser(RoleNames.Student);

            await Reporting(student).Submit(new SafetyReportViewModel { Building = "SV", Description = "Leak", Photo = PhotoFile() });

            var activity = Assert.Single(_db.NewContext().IncidentReportActivities.ToList());
            Assert.Equal(IncidentReportActivity.Received, activity.Action);
            Assert.Equal(student.UserID, activity.ActorID);
            Assert.Equal("Test Student", activity.ActorName);
            Assert.Equal("Pending", activity.ToStatus);
        }

        [Fact]
        public async Task Submit_WithoutPhoto_IsRejected()
        {
            var student = _db.AddUser(RoleNames.Student);

            var result = await Reporting(student).Submit(new SafetyReportViewModel { Building = "SV", Description = "Leak" });

            Assert.IsType<ViewResult>(result);
            Assert.Empty(_db.NewContext().IncidentReports.ToList());
        }

        [Fact]
        public async Task Submit_Anonymous_HidesReporter()
        {
            var student = _db.AddUser(RoleNames.Student);

            await Reporting(student).Submit(new SafetyReportViewModel { Building = "SV", Description = "Leak", Photo = PhotoFile(), IsAnonymous = true });

            var activity = Assert.Single(_db.NewContext().IncidentReportActivities.ToList());
            Assert.Null(activity.ActorID);
            Assert.Equal("Anonymous", activity.ActorName);
        }

        [Fact]
        public async Task UpdateStatus_StatusChange_RecordsFromAndTo()
        {
            var staff = _db.AddUser(RoleNames.Staff);
            var report = AddReport();

            await Staff(staff).UpdateStatus(report.ReportID, "Resolved", "Replaced the bulb");

            var activity = Assert.Single(Activities(report.ReportID));
            Assert.Equal(IncidentReportActivity.StatusChanged, activity.Action);
            Assert.Equal("Pending", activity.FromStatus);
            Assert.Equal("Resolved", activity.ToStatus);
            Assert.Equal("Replaced the bulb", activity.Note);
            Assert.Equal(staff.UserID, activity.ActorID);
            Assert.Equal("Test Staff", activity.ActorName);
        }

        [Fact]
        public async Task UpdateStatus_NoteOnly_RecordsNoteUpdated()
        {
            var staff = _db.AddUser(RoleNames.Staff);
            var report = AddReport(status: "Investigating");

            await Staff(staff).UpdateStatus(report.ReportID, "Investigating", "Called maintenance");

            var activity = Assert.Single(Activities(report.ReportID));
            Assert.Equal(IncidentReportActivity.NoteUpdated, activity.Action);
            Assert.Equal("Called maintenance", activity.Note);
        }

        [Fact]
        public async Task UpdateStatus_NothingChanged_RecordsNothing()
        {
            var staff = _db.AddUser(RoleNames.Staff);
            var report = AddReport();

            await Staff(staff).UpdateStatus(report.ReportID, "Pending", "  ");

            Assert.Empty(Activities(report.ReportID));
        }

        [Fact]
        public async Task UpdateStatus_KeepsEarlierNotes()
        {
            var staff = _db.AddUser(RoleNames.Staff);
            var report = AddReport();
            var controller = Staff(staff);

            await controller.UpdateStatus(report.ReportID, "Investigating", "First look");
            await controller.UpdateStatus(report.ReportID, "Resolved", "Fixed");

            Assert.Equal(new[] { "First look", "Fixed" },
                Activities(report.ReportID).Select(a => a.Note));
        }

        [Fact]
        public async Task Activity_Mine_ShowsOnlyMyWork()
        {
            var me = _db.AddUser(RoleNames.Staff);
            var colleague = _db.AddUser(RoleNames.Staff);
            var first = AddReport();
            var second = AddReport();

            await Staff(colleague).UpdateStatus(second.ReportID, "Dismissed", "Duplicate");
            var controller = Staff(me);
            await controller.UpdateStatus(first.ReportID, "Resolved", "Done");

            var result = (ViewResult)await controller.Activity(new StaffActivityFilterViewModel());
            var model = (StaffActivityViewModel)result.Model!;

            var row = Assert.Single(model.Rows);
            Assert.Equal(first.ReportID, row.ReportID);
            Assert.Equal(1, model.ResolvedCount);
            Assert.Equal(0, model.DismissedCount);
            Assert.Equal(1, model.ActionCount);
        }

        [Fact]
        public async Task Activity_All_ShowsEveryonesWorkAndReceived()
        {
            var me = _db.AddUser(RoleNames.Staff);
            var colleague = _db.AddUser(RoleNames.Staff);
            var student = _db.AddUser(RoleNames.Student);

            await Reporting(student).Submit(new SafetyReportViewModel { Building = "SV", Description = "Leak", Photo = PhotoFile() });
            var reportId = _db.NewContext().IncidentReports.Single().ReportID;
            await Staff(colleague).UpdateStatus(reportId, "Dismissed", "Duplicate");

            var result = (ViewResult)await Staff(me).Activity(
                new StaffActivityFilterViewModel { Scope = StaffActivityFilterViewModel.All });
            var model = (StaffActivityViewModel)result.Model!;

            Assert.Equal(2, model.Rows.Count);
            Assert.Equal(1, model.ReceivedCount);
            Assert.Equal(1, model.DismissedCount);
        }

        [Fact]
        public async Task Activity_FiltersByStatusBuildingAndDate()
        {
            var me = _db.AddUser(RoleNames.Staff);
            var sv = AddReport("SV");
            var st = AddReport("ST");
            var old = AddReport("SV");
            var controller = Staff(me);

            await controller.UpdateStatus(sv.ReportID, "Resolved", "a");
            await controller.UpdateStatus(st.ReportID, "Resolved", "b");
            await controller.UpdateStatus(old.ReportID, "Resolved", "c");
            var oldRow = _db.Context.IncidentReportActivities.Single(a => a.ReportID == old.ReportID);
            oldRow.CreatedAt = DateTime.Today.AddDays(-30);
            await controller.UpdateStatus(sv.ReportID, "Investigating", "reopened");
            _db.Context.SaveChanges();

            var result = (ViewResult)await controller.Activity(new StaffActivityFilterViewModel
            {
                Status = "Resolved",
                Building = "SV",
                From = DateTime.Today.AddDays(-7),
                To = DateTime.Today
            });
            var model = (StaffActivityViewModel)result.Model!;

            var row = Assert.Single(model.Rows);
            Assert.Equal(sv.ReportID, row.ReportID);
        }

        [Fact]
        public async Task Activity_Student_Redirected()
        {
            var student = _db.AddUser(RoleNames.Student);
            LogIn(student, RoleNames.Student);
            var controller = Wire(new StaffController(_db.Context));

            var result = await controller.Activity(new StaffActivityFilterViewModel());

            var redirect = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("Login", redirect.ActionName);
        }
    }
}
