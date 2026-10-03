using System.Text.Json;
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
    public class AuditLogTests : IDisposable
    {
        private readonly TestDb _db = new();
        private readonly FakeSession _session = new();
        private readonly User _admin;

        public AuditLogTests()
        {
            _admin = _db.AddUser(RoleNames.Administrator);
            _session.SetString("UserID", _admin.UserID.ToString());
            _session.SetString("UserName", "Ada Admin");
            _session.SetString("RoleName", RoleNames.Administrator);
        }

        public void Dispose() => _db.Dispose();

        private (HttpContext Http, AuditService Audit) Request()
        {
            var http = FakeSession.HttpContextWith(_session);
            return (http, new AuditService(_db.Context, new HttpContextAccessor { HttpContext = http }));
        }

        private AdminController Admin()
        {
            var (http, audit) = Request();
            var controller = new AdminController(
                _db.Context, new FakeEmailService(), NullLogger<AdminController>.Instance,
                new HierarchyService(_db.Context), new PlacementService(_db.Context), audit);
            controller.ControllerContext = new ControllerContext { HttpContext = http };
            controller.TempData = new TempDataDictionary(http, new NullTempDataProvider());
            return controller;
        }

        private AcademicStructureController Structure()
        {
            var (http, audit) = Request();
            var controller = new AcademicStructureController(
                _db.Context, new HierarchyService(_db.Context), audit);
            controller.ControllerContext = new ControllerContext { HttpContext = http };
            controller.TempData = new TempDataDictionary(http, new NullTempDataProvider());
            controller.Url = new FakeUrlHelper();
            return controller;
        }

        private static AdminUserFormViewModel FormFor(User user) => new()
        {
            UserID = user.UserID,
            FirstName = user.FirstName,
            LastName = user.LastName,
            Email = user.Email,
            RoleID = user.RoleID,
            CollegeID = user.CollegeID,
            DepartmentID = user.DepartmentID,
            ProgramID = user.ProgramID,
            IsActive = user.IsActive
        };

        [Fact]
        public async Task EditUser_RecordsOnlyWhatChanged()
        {
            var college = _db.AddCollege("College of Computing and Information Technology");
            var dept = _db.AddDepartment(college, "Information Technology");
            var faculty = _db.AddUser(RoleNames.Faculty, college.CollegeID, dept.DepartmentID);

            var form = FormFor(faculty);
            form.IsActive = false;
            form.Password = "NewPass123";
            await Admin().EditUser(faculty.UserID, form);

            var log = _db.NewContext().AuditLogs.Single();
            Assert.Equal("Update", log.Action);
            Assert.Equal(AuditArea.Users, log.TableAffected);
            Assert.Equal(faculty.UserID, log.RecordID);
            Assert.Equal(_admin.UserID, log.UserID);
            Assert.Equal("Ada Admin", log.ActorName);
            Assert.Contains("Status", log.Summary);
            Assert.Contains("Password", log.Summary);
            Assert.DoesNotContain("Email", log.Summary);

            var before = JsonSerializer.Deserialize<Dictionary<string, string?>>(log.OldValues!)!;
            var after = JsonSerializer.Deserialize<Dictionary<string, string?>>(log.NewValues!)!;
            Assert.Equal("Active", before["Status"]);
            Assert.Equal("Inactive", after["Status"]);
            Assert.Equal("reset", after["Password"]);
            Assert.DoesNotContain("NewPass123", log.NewValues);
        }

        [Fact]
        public async Task EditUser_NothingChanged_NoRecord()
        {
            var staff = _db.AddUser(RoleNames.Staff);

            await Admin().EditUser(staff.UserID, FormFor(staff));

            Assert.Empty(_db.NewContext().AuditLogs);
        }

        [Fact]
        public async Task DeleteUser_KeepsTheirAuditRowsAndRecordsTheDelete()
        {
            var formerAdmin = _db.AddUser(RoleNames.Administrator);
            _db.Context.AuditLogs.Add(new AuditLog
            {
                UserID = formerAdmin.UserID,
                ActorName = "Former Admin",
                Action = "Approve",
                TableAffected = AuditArea.Users,
                Summary = "Approved someone."
            });
            _db.Context.SaveChanges();

            await Admin().DeleteUser(formerAdmin.UserID);

            var logs = _db.NewContext().AuditLogs.OrderBy(l => l.LogID).ToList();
            Assert.Equal(2, logs.Count);
            Assert.Null(logs[0].UserID);
            Assert.Equal("Former Admin", logs[0].ActorName);
            Assert.Equal("Delete", logs[1].Action);
            Assert.Equal(formerAdmin.UserID, logs[1].RecordID);
            Assert.Equal(_admin.UserID, logs[1].UserID);
        }

        [Fact]
        public async Task RetireProgram_Recorded()
        {
            var college = _db.AddCollege("College of Science");
            var dept = _db.AddDepartment(college, "Biology");
            var program = _db.AddProgram(dept, "BS Biology");

            await Structure().SetProgramActive(program.ProgramID, college.CollegeID, active: false);

            var log = _db.NewContext().AuditLogs.Single();
            Assert.Equal("Retire", log.Action);
            Assert.Equal(AuditArea.Programs, log.TableAffected);
            Assert.Equal(program.ProgramID, log.RecordID);
            Assert.Contains("BS Biology", log.Summary);
        }

        [Fact]
        public async Task RetireRefused_NotRecorded()
        {
            var college = _db.AddCollege("College of Science");
            var dept = _db.AddDepartment(college, "Biology");
            var program = _db.AddProgram(dept, "BS Biology");
            _db.AddUser(RoleNames.Student, college.CollegeID, dept.DepartmentID, program.ProgramID);

            await Structure().SetProgramActive(program.ProgramID, college.CollegeID, active: false);

            Assert.Empty(_db.NewContext().AuditLogs);
        }

        [Fact]
        public async Task AuditLogPage_FiltersByArea()
        {
            _db.Context.AuditLogs.AddRange(
                new AuditLog { ActorName = "A", Action = "Update", TableAffected = AuditArea.Users, Summary = "u" },
                new AuditLog { ActorName = "B", Action = "Retire", TableAffected = AuditArea.Programs, Summary = "p" });
            _db.Context.SaveChanges();

            var result = await Admin().AuditLog(null, AuditArea.Programs, null, null, null);

            var model = Assert.IsType<AuditLogViewModel>(Assert.IsType<ViewResult>(result).Model);
            Assert.Equal(1, model.TotalCount);
            Assert.Equal("p", model.Rows.Single().Summary);
        }
    }
}
