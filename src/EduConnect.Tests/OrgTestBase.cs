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
    // Shared setup for the organization tests: one CCIT program, an admin,
    // a faculty member and two organizations.
    public abstract class OrgTestBase : IDisposable
    {
        protected readonly TestDb _db = new();
        protected readonly FakeSession _session = new();
        protected readonly FakeNotificationService _notifications = new();
        protected readonly User _admin, _faculty;
        protected readonly AcademicProgram _bsit;
        protected readonly Organization _org, _otherOrg;

        protected OrgTestBase()
        {
            var ccit = _db.AddCollege("CCIT");
            var itis = _db.AddDepartment(ccit, "IT&IS");
            _bsit = _db.AddProgram(itis, "BSIT");
            _admin = _db.AddUser(RoleNames.Administrator);
            _faculty = _db.AddUser(RoleNames.Faculty, ccit.CollegeID, itis.DepartmentID);
            _org = AddOrg("Gaming Society");
            _otherOrg = AddOrg("IT Society");
        }

        public void Dispose() => _db.Dispose();

        protected Organization AddOrg(string name)
        {
            var org = new Organization { OrgName = name, CreatedByID = _admin.UserID };
            _db.Context.Organizations.Add(org);
            _db.Context.SaveChanges();
            return org;
        }

        protected User Student(string first = "Juan", string last = "Cruz", string? number = null,
            bool verified = true, bool active = true, int? programId = null)
        {
            var s = _db.AddUser(RoleNames.Student, programId: programId ?? _bsit.ProgramID, isActive: active);
            s.FirstName = first;
            s.LastName = last;
            s.StudentID = number;
            s.VerificationStatus = verified ? VerificationStatuses.Verified : VerificationStatuses.Pending;
            _db.Context.SaveChanges();
            return s;
        }

        protected OrgMember Join(Organization org, User user, string role = OrgRoles.Member, bool active = true)
        {
            var m = new OrgMember { OrgID = org.OrgID, UserID = user.UserID, OrgRole = role, IsActive = active };
            _db.Context.OrgMembers.Add(m);
            _db.Context.SaveChanges();
            return m;
        }

        protected OrgFollower Follow(Organization org, User user)
        {
            var f = new OrgFollower { OrgID = org.OrgID, UserID = user.UserID };
            _db.Context.OrgFollowers.Add(f);
            _db.Context.SaveChanges();
            return f;
        }

        protected static IFormFile Upload(string fileName, string contentType, int bytes = 10) =>
            new FormFile(new MemoryStream(new byte[bytes]), 0, bytes, "file", fileName)
            {
                Headers = new HeaderDictionary(),
                ContentType = contentType
            };

        protected OrgController Controller(User as_, string role)
        {
            _session.SetString("UserID", as_.UserID.ToString());
            _session.SetString("RoleName", role);
            _session.SetString("UserName", $"{as_.FirstName} {as_.LastName}");
            var http = FakeSession.HttpContextWith(_session);
            var controller = new OrgController(
                _db.Context, NullLogger<OrgController>.Instance, null!, new FakeBlobStorage(),
                new OrgAccess(_db.Context),
                new AuditService(_db.Context, new HttpContextAccessor { HttpContext = http }),
                _notifications, new PlacementService(_db.Context));
            controller.ControllerContext = new ControllerContext { HttpContext = http };
            controller.TempData = new TempDataDictionary(http, new NullTempDataProvider());
            controller.Url = new FakeUrlHelper();
            return controller;
        }

        protected OrgController AsAdmin() => Controller(_admin, RoleNames.Administrator);

        protected static OrgPostViewModel APost() => new() { Title = "General assembly", Body = "Friday 3pm" };
    }
}
