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
    public class ReportingSanctionTests : IDisposable
    {
        private readonly TestDb _db = new();
        private readonly FakeSession _session = new();
        private readonly FakeNotificationService _notes = new();
        private readonly FakeEmailService _mail = new();
        private readonly User _student, _staff, _admin;

        public ReportingSanctionTests()
        {
            _student = _db.AddUser(RoleNames.Student);
            _staff = _db.AddUser(RoleNames.Staff);
            _admin = _db.AddUser(RoleNames.Administrator);
        }

        public void Dispose() => _db.Dispose();

        private void LogIn(User user, string role)
        {
            _session.Clear();
            _session.SetString("UserID", user.UserID.ToString());
            _session.SetString("UserName", $"{user.FirstName} {user.LastName}");
            _session.SetString("RoleName", role);
        }

        private ReportingSanctionService Service() =>
            new(_db.Context, _notes, _mail,
                new AuditService(_db.Context, new HttpContextAccessor { HttpContext = FakeSession.HttpContextWith(_session) }));

        private T Wire<T>(T controller) where T : Controller
        {
            controller.ControllerContext = new ControllerContext { HttpContext = FakeSession.HttpContextWith(_session) };
            controller.TempData = new TempDataDictionary(controller.ControllerContext.HttpContext, new NullTempDataProvider());
            return controller;
        }

        private StaffController Staff(User user, string role = RoleNames.Staff)
        {
            LogIn(user, role);
            return Wire(new StaffController(_db.Context, _notes, Service()));
        }

        private SafetyReportController Reporting(User user)
        {
            LogIn(user, RoleNames.Student);
            return Wire(new SafetyReportController(
                _db.Context, null!, new FakeNotificationService(), new FakeEmailService(),
                NullLogger<SafetyReportController>.Instance, new FakeBlobStorage(), Service()));
        }

        private IncidentReport Report(User? by = null, string status = "Pending", bool anonymous = false)
        {
            var report = new IncidentReport
            {
                ReportedByID = (by ?? _student).UserID,
                IncidentType = "SV",
                Description = "Broken light",
                Status = status,
                IsAnonymous = anonymous
            };
            _db.Context.IncidentReports.Add(report);
            _db.Context.SaveChanges();
            return report;
        }

        private async Task<ReportingSanction> MarkFalse(IncidentReport report, string reason = "No damage found")
        {
            LogIn(_staff, RoleNames.Staff);
            var result = await Service().MarkFalseAsync(report.ReportID, reason, _staff.UserID);
            Assert.True(result.Ok, result.Error);
            return _db.NewContext().ReportingSanctions.Single(s => s.ReportID == report.ReportID);
        }

        private void Backdate(ReportingSanction sanction, DateTime issuedAt, DateTime? endsAt = null)
        {
            var row = _db.Context.ReportingSanctions.Single(s => s.SanctionID == sanction.SanctionID);
            row.IssuedAt = issuedAt;
            row.EndsAt = endsAt;
            _db.Context.SaveChanges();
        }

        // ─── The ladder ────────────────────────

        [Fact]
        public async Task FirstFalseReport_IsAWarning()
        {
            var report = Report();

            var s = await MarkFalse(report);

            Assert.Equal((ReportingSanction.Warning, 1, (DateTime?)null, false), (s.Type, s.Strike, s.EndsAt, s.ReferredToOsa));
            Assert.Equal("No damage found", s.Reason);
            Assert.Equal("Pending", s.ReportStatusBefore);
            var saved = _db.NewContext().IncidentReports.Single(r => r.ReportID == report.ReportID);
            Assert.Equal(StaffController.FalseReport, saved.Status);
            Assert.Contains(_db.NewContext().IncidentReportActivities,
                a => a.ReportID == report.ReportID && a.Action == IncidentReportActivity.MarkedFalse && a.Note == "No damage found");
            Assert.Contains(_notes.Sent, n => n.UserId == _student.UserID && n.Type == "SafetyReport");
            Assert.Contains(_mail.Sent, m => m.Contains(_student.Email));
            Assert.Contains(_db.NewContext().AuditLogs, l => l.TableAffected == AuditArea.SafetyReports && l.Action == "Warning");
        }

        [Fact]
        public async Task SecondWithinAYear_IsASevenDaySuspension()
        {
            await MarkFalse(Report());
            var before = DateTime.Now;

            var s = await MarkFalse(Report());

            Assert.Equal((ReportingSanction.Suspension, 2, false), (s.Type, s.Strike, s.ReferredToOsa));
            Assert.InRange(s.EndsAt!.Value, before.AddDays(7), DateTime.Now.AddDays(7));
        }

        [Fact]
        public async Task ThirdWithinAYear_IsThirtyDaysAndAnOsaReferral()
        {
            await MarkFalse(Report());
            var second = await MarkFalse(Report());
            Backdate(second, DateTime.Now.AddDays(-20), DateTime.Now.AddDays(-13));   // served
            var before = DateTime.Now;

            var s = await MarkFalse(Report());

            Assert.Equal((ReportingSanction.Suspension, 3, true), (s.Type, s.Strike, s.ReferredToOsa));
            Assert.InRange(s.EndsAt!.Value, before.AddDays(30), DateTime.Now.AddDays(30));
        }

        [Fact]
        public async Task NewSuspensionWhileSuspended_StartsWhenTheCurrentOneEnds()
        {
            await MarkFalse(Report());
            var second = await MarkFalse(Report());

            var third = await MarkFalse(Report());

            Assert.Equal(second.EndsAt!.Value.AddDays(30), third.EndsAt);
        }

        [Fact]
        public async Task FalseReportsOlderThanAYear_DoNotCount()
        {
            var old = await MarkFalse(Report());
            Backdate(old, DateTime.Now.AddMonths(-13));

            var s = await MarkFalse(Report());

            Assert.Equal((ReportingSanction.Warning, 1), (s.Type, s.Strike));
        }

        [Fact]
        public async Task RevokedSanctions_DoNotCount()
        {
            var first = await MarkFalse(Report());
            LogIn(_admin, RoleNames.Administrator);
            Assert.True((await Service().RevokeAsync(first.SanctionID, "Report was genuine", _admin.UserID)).Ok);

            var s = await MarkFalse(Report());

            Assert.Equal((ReportingSanction.Warning, 1), (s.Type, s.Strike));
        }

        // ─── Who can be sanctioned ─────────────

        [Fact]
        public async Task NonStudentReporter_IsMarkedButNotSanctioned()
        {
            var faculty = _db.AddUser(RoleNames.Faculty);
            var report = Report(by: faculty);

            LogIn(_staff, RoleNames.Staff);
            Assert.True((await Service().MarkFalseAsync(report.ReportID, "No damage found", _staff.UserID)).Ok);

            Assert.Equal(StaffController.FalseReport, _db.NewContext().IncidentReports.Single(r => r.ReportID == report.ReportID).Status);
            Assert.Empty(_db.NewContext().ReportingSanctions);
        }

        [Fact]
        public async Task LegacyAnonymousReport_IsMarkedButNotSanctioned()
        {
            var report = Report(anonymous: true);

            LogIn(_staff, RoleNames.Staff);
            Assert.True((await Service().MarkFalseAsync(report.ReportID, "No damage found", _staff.UserID)).Ok);

            Assert.Empty(_db.NewContext().ReportingSanctions);
        }

        [Fact]
        public async Task MarkFalse_NeedsAReason_AndOnlyOnce()
        {
            var report = Report();
            LogIn(_staff, RoleNames.Staff);

            Assert.False((await Service().MarkFalseAsync(report.ReportID, "  ", _staff.UserID)).Ok);
            await MarkFalse(report);
            Assert.False((await Service().MarkFalseAsync(report.ReportID, "Again", _staff.UserID)).Ok);
            Assert.Single(_db.NewContext().ReportingSanctions);
        }

        [Fact]
        public async Task Preview_DescribesTheNextStep()
        {
            await MarkFalse(Report());

            var preview = await Service().PreviewFalseReportAsync(Report().ReportID);

            Assert.True(preview.Sanctions);
            Assert.Equal(2, preview.Strike);
            Assert.Contains("7-day suspension", preview.Summary);
        }

        // ─── Enforcement ───────────────────────

        [Fact]
        public async Task Suspended_CannotFileANewReport()
        {
            await MarkFalse(Report());
            await MarkFalse(Report());
            var count = _db.NewContext().IncidentReports.Count();

            var get = await Reporting(_student).Submit();
            var post = await Reporting(_student).Submit(new SafetyReportViewModel { Building = "SV", Description = "Leak" });

            Assert.Equal("Suspended", Assert.IsType<ViewResult>(get).ViewName);
            Assert.Equal("Suspended", Assert.IsType<ViewResult>(post).ViewName);
            Assert.Equal(count, _db.NewContext().IncidentReports.Count());
        }

        [Fact]
        public async Task SuspensionExpiresByItself()
        {
            await MarkFalse(Report());
            var second = await MarkFalse(Report());
            Backdate(second, DateTime.Now.AddDays(-8), DateTime.Now.AddMinutes(-1));

            var get = await Reporting(_student).Submit();

            Assert.NotEqual("Suspended", Assert.IsType<ViewResult>(get).ViewName);
        }

        [Fact]
        public async Task Suspended_StillSeesTheirReportsAndSanctions()
        {
            await MarkFalse(Report());
            await MarkFalse(Report());

            var view = Assert.IsType<ViewResult>(await Reporting(_student).MyReports());

            Assert.Equal(2, Assert.IsAssignableFrom<IEnumerable<IncidentReport>>(view.Model).Count());
            Assert.Equal(2, ((List<ReportingSanction>)view.ViewData["Sanctions"]!).Count);
        }

        [Fact]
        public async Task StaffStatusDropdown_CannotUndoAFalseReport()
        {
            var report = Report();
            await MarkFalse(report);

            var result = await Staff(_staff).UpdateStatus(report.ReportID, "Resolved", null);

            Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal(StaffController.FalseReport, _db.NewContext().IncidentReports.Single(r => r.ReportID == report.ReportID).Status);
        }

        [Fact]
        public async Task StaffController_MarkFalse_RecordsIt()
        {
            var report = Report();

            await Staff(_staff).MarkFalse(report.ReportID, "Photo is from another campus");

            Assert.Equal("Photo is from another campus", _db.NewContext().ReportingSanctions.Single().Reason);
        }

        // ─── Appeal and reversal ───────────────

        [Fact]
        public async Task Appeal_OncePerSanction_OnlyByTheStudent()
        {
            var s = await MarkFalse(Report());
            var other = _db.AddUser(RoleNames.Student);

            Assert.False((await Service().AppealAsync(s.SanctionID, other.UserID, "Not me")).Ok);
            Assert.True((await Service().AppealAsync(s.SanctionID, _student.UserID, "The light really was broken")).Ok);
            Assert.False((await Service().AppealAsync(s.SanctionID, _student.UserID, "Again")).Ok);

            var saved = _db.NewContext().ReportingSanctions.Single();
            Assert.Equal("The light really was broken", saved.AppealText);
            Assert.True(saved.AppealPending);
            Assert.Contains(_notes.Sent, n => n.UserId == _admin.UserID);
        }

        [Fact]
        public async Task Revoke_LiftsTheSuspensionAndRestoresTheReport()
        {
            var first = await MarkFalse(Report(status: "Investigating"));
            var report = Report(status: "Investigating");
            var second = await MarkFalse(report);
            await Service().AppealAsync(second.SanctionID, _student.UserID, "It was real");
            _notes.Sent.Clear();

            LogIn(_admin, RoleNames.Administrator);
            Assert.True((await Service().RevokeAsync(second.SanctionID, "Maintenance confirmed the leak", _admin.UserID)).Ok);

            var saved = _db.NewContext().ReportingSanctions.Single(s => s.SanctionID == second.SanctionID);
            Assert.True(saved.IsRevoked);
            Assert.False(saved.AppealPending);
            Assert.Equal("Investigating", _db.NewContext().IncidentReports.Single(r => r.ReportID == report.ReportID).Status);
            Assert.Contains(_db.NewContext().IncidentReportActivities,
                a => a.ReportID == report.ReportID && a.Action == IncidentReportActivity.FalseReversed);
            Assert.Contains(_notes.Sent, n => n.UserId == _student.UserID);
            Assert.Contains(_db.NewContext().AuditLogs, l => l.Action == "Revoke");
            Assert.NotEqual("Suspended", Assert.IsType<ViewResult>(await Reporting(_student).Submit()).ViewName);
        }

        [Fact]
        public async Task UpholdAppeal_KeepsTheSanction()
        {
            var s = await MarkFalse(Report());
            await Service().AppealAsync(s.SanctionID, _student.UserID, "It was real");

            LogIn(_admin, RoleNames.Administrator);
            Assert.True((await Service().UpholdAppealAsync(s.SanctionID, "Photo is from 2023", _admin.UserID)).Ok);

            var saved = _db.NewContext().ReportingSanctions.Single();
            Assert.False(saved.IsRevoked);
            Assert.False(saved.AppealPending);
            Assert.Equal("Photo is from 2023", saved.AppealUpheldNote);
        }

        [Fact]
        public async Task OnlyAnAdministratorCanRevoke()
        {
            var s = await MarkFalse(Report());

            await Staff(_staff).RevokeSanction(s.SanctionID, "Staff trying");
            Assert.False(_db.NewContext().ReportingSanctions.Single().IsRevoked);

            await Staff(_admin, RoleNames.Administrator).RevokeSanction(s.SanctionID, "Genuine after all");
            Assert.True(_db.NewContext().ReportingSanctions.Single().IsRevoked);
        }
    }
}
