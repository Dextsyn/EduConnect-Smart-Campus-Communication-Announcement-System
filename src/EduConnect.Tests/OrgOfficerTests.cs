using System.Text.Json;
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
    public class OrgOfficerTests : IDisposable
    {
        private readonly TestDb _db = new();
        private readonly FakeSession _session = new();
        private readonly FakeNotificationService _notifications = new();
        private readonly User _admin, _faculty;
        private readonly AcademicProgram _bsit;
        private readonly Organization _org, _otherOrg;

        public OrgOfficerTests()
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

        private Organization AddOrg(string name)
        {
            var org = new Organization { OrgName = name, CreatedByID = _admin.UserID };
            _db.Context.Organizations.Add(org);
            _db.Context.SaveChanges();
            return org;
        }

        private User Student(string first = "Juan", string last = "Cruz", string? number = null,
            bool verified = true, bool active = true)
        {
            var s = _db.AddUser(RoleNames.Student, programId: _bsit.ProgramID, isActive: active);
            s.FirstName = first;
            s.LastName = last;
            s.StudentID = number;
            s.VerificationStatus = verified ? VerificationStatuses.Verified : VerificationStatuses.Pending;
            _db.Context.SaveChanges();
            return s;
        }

        private OrgMember Join(Organization org, User user, string role = OrgRoles.Member, bool active = true)
        {
            var m = new OrgMember { OrgID = org.OrgID, UserID = user.UserID, OrgRole = role, IsActive = active };
            _db.Context.OrgMembers.Add(m);
            _db.Context.SaveChanges();
            return m;
        }

        private OrgController Controller(User as_, string role)
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
            return controller;
        }

        private OrgController AsAdmin() => Controller(_admin, RoleNames.Administrator);

        private static JsonElement Json(IActionResult result) =>
            JsonSerializer.SerializeToElement(Assert.IsType<JsonResult>(result).Value);

        private static string[] Names(JsonElement json) =>
            json.GetProperty("results").EnumerateArray()
                .Select(r => r.GetProperty("name").GetString()!).ToArray();

        private OrgMember Reload(OrgMember m) =>
            _db.NewContext().OrgMembers.Single(x => x.MemberID == m.MemberID);

        private static OrgPostViewModel APost() => new() { Title = "General assembly", Body = "Friday 3pm" };

        // ─── Posting ───────────────────────────

        [Fact]
        public async Task Officer_posts_directly_and_members_are_notified()
        {
            var officer = Student("Ana", "Reyes");
            var member = Student("Ben", "Lim");
            var inactiveMember = Student("Cy", "Tan", active: false);
            var outsider = Student("Dee", "Uy");
            Join(_org, officer, OrgRoles.Officer);
            Join(_org, member);
            Join(_org, inactiveMember);
            Join(_otherOrg, outsider);

            var result = await Controller(officer, RoleNames.Student).Post(_org.OrgID, APost());

            Assert.IsType<RedirectToActionResult>(result);
            var post = Assert.Single(_db.NewContext().OrgAnnouncements);
            Assert.Equal(officer.UserID, post.PostedByID);
            Assert.Equal(new[] { member.UserID }, _notifications.Sent.Select(s => s.UserId));
            Assert.All(_notifications.Sent, s => Assert.Equal("OrgAnnouncement", s.Type));
        }

        [Fact]
        public async Task Retired_faculty_adviser_can_no_longer_post()
        {
            Join(_org, _faculty, OrgRoles.Adviser, active: false);

            await Controller(_faculty, RoleNames.Faculty).Post(_org.OrgID, APost());

            Assert.Empty(_db.NewContext().OrgAnnouncements);
        }

        [Fact]
        public async Task Plain_member_cannot_post()
        {
            var member = Student();
            Join(_org, member);

            await Controller(member, RoleNames.Student).Post(_org.OrgID, APost());

            Assert.Empty(_db.NewContext().OrgAnnouncements);
        }

        [Fact]
        public async Task Officer_of_another_org_cannot_post_here()
        {
            var officer = Student();
            Join(_otherOrg, officer, OrgRoles.Officer);

            await Controller(officer, RoleNames.Student).Post(_org.OrgID, APost());

            Assert.Empty(_db.NewContext().OrgAnnouncements);
        }

        [Fact]
        public async Task Deactivated_officer_loses_posting_at_once()
        {
            var officer = Student();
            Join(_org, officer, OrgRoles.Officer);
            officer.IsActive = false;          // signed in before the admin deactivated them
            _db.Context.SaveChanges();

            await Controller(officer, RoleNames.Student).Post(_org.OrgID, APost());

            Assert.Empty(_db.NewContext().OrgAnnouncements);
        }

        [Fact]
        public async Task Officer_cannot_post_for_a_deactivated_org()
        {
            var officer = Student();
            Join(_org, officer, OrgRoles.Officer);
            _org.IsActive = false;
            _db.Context.SaveChanges();

            Assert.False(await new OrgAccess(_db.Context).IsOfficerAsync(officer.UserID, _org.OrgID));
        }

        // ─── Membership ────────────────────────

        [Fact]
        public async Task Admin_adds_a_verified_student_and_it_is_audited()
        {
            var s = Student(number: "202300001");

            await AsAdmin().AddMember(_org.OrgID, s.UserID);

            var row = Assert.Single(_db.NewContext().OrgMembers);
            Assert.Equal(OrgRoles.Member, row.OrgRole);
            Assert.True(row.IsActive);
            var log = Assert.Single(_db.NewContext().AuditLogs);
            Assert.Equal(AuditArea.Organizations, log.TableAffected);
            Assert.Equal("Add member", log.Action);
            Assert.Equal(_admin.UserID, log.UserID);
            Assert.Contains("202300001", log.Summary);
        }

        [Theory]
        [InlineData(false, true)]   // registration not verified
        [InlineData(true, false)]   // deactivated account
        public async Task Only_active_verified_students_can_be_added(bool verified, bool active)
        {
            var s = Student(verified: verified, active: active);

            await AsAdmin().AddMember(_org.OrgID, s.UserID);
            await AsAdmin().AddMember(_org.OrgID, _faculty.UserID);

            Assert.Empty(_db.NewContext().OrgMembers);
        }

        [Fact]
        public async Task Removing_a_member_ends_their_officer_role()
        {
            var s = Student();
            var row = Join(_org, s, OrgRoles.Officer);
            row.Position = "President";
            _db.Context.SaveChanges();

            await AsAdmin().RemoveMember(_org.OrgID, row.MemberID);

            var saved = Reload(row);
            Assert.False(saved.IsActive);
            Assert.Equal(OrgRoles.Member, saved.OrgRole);
            Assert.Null(saved.Position);
            Assert.False(await new OrgAccess(_db.Context).IsOfficerAsync(s.UserID, _org.OrgID));
        }

        [Fact]
        public async Task Re_adding_a_former_member_reuses_their_row()
        {
            var s = Student();
            var row = Join(_org, s, active: false);

            await AsAdmin().AddMember(_org.OrgID, s.UserID);

            Assert.True(Reload(row).IsActive);
            Assert.Single(_db.NewContext().OrgMembers);
        }

        // ─── Officer assignment ────────────────

        [Fact]
        public async Task Admin_assigns_a_member_as_officer_with_a_title()
        {
            var s = Student();
            var row = Join(_org, s);

            await AsAdmin().AssignOfficer(_org.OrgID, row.MemberID, "  President ");

            var saved = Reload(row);
            Assert.Equal(OrgRoles.Officer, saved.OrgRole);
            Assert.Equal("President", saved.Position);
            Assert.Equal("Assign officer", _db.NewContext().AuditLogs.Single().Action);
        }

        [Fact]
        public async Task Several_officers_per_org_are_allowed()
        {
            var a = Join(_org, Student("Ana", "Reyes"));
            var b = Join(_org, Student("Ben", "Lim"));

            await AsAdmin().AssignOfficer(_org.OrgID, a.MemberID, null);
            await AsAdmin().AssignOfficer(_org.OrgID, b.MemberID, null);

            Assert.Equal(2, _db.NewContext().OrgMembers.Count(m => m.OrgRole == OrgRoles.Officer));
        }

        [Fact]
        public async Task A_student_is_an_officer_of_one_org_only()
        {
            var s = Student();
            Join(_otherOrg, s, OrgRoles.Officer);
            var row = Join(_org, s);

            var controller = AsAdmin();
            await controller.AssignOfficer(_org.OrgID, row.MemberID, null);

            Assert.Equal(OrgRoles.Member, Reload(row).OrgRole);
            Assert.Contains("IT Society", (string)controller.TempData["Error"]!);
        }

        [Fact]
        public void The_database_also_refuses_a_second_officer_post()
        {
            var s = Student();
            Join(_otherOrg, s, OrgRoles.Officer);

            Assert.Throws<DbUpdateException>(() => Join(_org, s, OrgRoles.Officer));
        }

        [Fact]
        public async Task A_member_of_another_org_cannot_be_assigned_here()
        {
            var row = Join(_otherOrg, Student());

            await AsAdmin().AssignOfficer(_org.OrgID, row.MemberID, null);

            Assert.Equal(OrgRoles.Member, Reload(row).OrgRole);
        }

        [Fact]
        public async Task A_deactivated_member_cannot_be_assigned()
        {
            var row = Join(_org, Student(active: false));

            await AsAdmin().AssignOfficer(_org.OrgID, row.MemberID, null);

            Assert.Equal(OrgRoles.Member, Reload(row).OrgRole);
        }

        [Fact]
        public async Task Unassigning_keeps_the_membership()
        {
            var row = Join(_org, Student(), OrgRoles.Officer);

            await AsAdmin().UnassignOfficer(_org.OrgID, row.MemberID);

            var saved = Reload(row);
            Assert.Equal(OrgRoles.Member, saved.OrgRole);
            Assert.True(saved.IsActive);
        }

        [Fact]
        public async Task Non_admins_cannot_manage_members()
        {
            var officer = Student();
            Join(_org, officer, OrgRoles.Officer);
            var other = Student("Ben", "Lim");
            var row = Join(_org, other);
            var asOfficer = Controller(officer, RoleNames.Student);

            await asOfficer.AddMember(_org.OrgID, Student("Cy", "Tan").UserID);
            await asOfficer.AssignOfficer(_org.OrgID, row.MemberID, null);
            var search = Json(await asOfficer.SearchMembers(_org.OrgID, "ben"));

            Assert.Equal(2, _db.NewContext().OrgMembers.Count());
            Assert.Equal(OrgRoles.Member, Reload(row).OrgRole);
            Assert.Equal("forbidden", search.GetProperty("error").GetString());
        }

        // ─── Search ────────────────────────────

        [Fact]
        public async Task Member_search_is_scoped_to_the_org_and_ignores_accents()
        {
            Join(_org, Student("José", "Peña", "202300001"));
            Join(_org, Student("Jose", "Inactive", active: false));
            Join(_otherOrg, Student("Jose", "Elsewhere"));
            Join(_org, Student("Jose", "Former"), active: false);

            var names = Names(Json(await AsAdmin().SearchMembers(_org.OrgID, "jose pena")));

            Assert.Equal(new[] { "José Peña" }, names);
        }

        [Fact]
        public async Task Member_search_matches_student_number_prefix_and_flags_officers()
        {
            var a = Student("Ana", "Reyes", "202300001");
            Join(_org, a, OrgRoles.Officer);
            var b = Student("Ben", "Lim", "202300002");
            Join(_org, b);
            Join(_otherOrg, b, OrgRoles.Officer);

            var results = Json(await AsAdmin().SearchMembers(_org.OrgID, "2023000"))
                .GetProperty("results").EnumerateArray().ToList();

            Assert.Equal(2, results.Count);
            var ana = results.Single(r => r.GetProperty("name").GetString() == "Ana Reyes");
            Assert.True(ana.GetProperty("isOfficer").GetBoolean());
            Assert.Equal("BSIT", ana.GetProperty("placement").GetString());
            var ben = results.Single(r => r.GetProperty("name").GetString() == "Ben Lim");
            Assert.Equal("IT Society", ben.GetProperty("officerOf").GetString());
        }

        [Fact]
        public async Task Student_search_skips_members_and_unverified_accounts()
        {
            Join(_org, Student("Maria", "Member"));
            Student("Maria", "Pending", verified: false);
            Student("Maria", "Clara");

            var names = Names(Json(await AsAdmin().SearchStudents(_org.OrgID, "maria")));

            Assert.Equal(new[] { "Maria Clara" }, names);
        }

        [Fact]
        public async Task Too_short_a_query_returns_nothing()
        {
            Join(_org, Student("Al", "Bo"));

            Assert.Empty(Names(Json(await AsAdmin().SearchMembers(_org.OrgID, "a"))));
        }
    }
}
