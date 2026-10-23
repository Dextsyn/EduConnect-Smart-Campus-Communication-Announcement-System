using EduConnect.Web;
using EduConnect.Web.Models;
using EduConnect.Web.Services;
using EduConnect.Web.ViewModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EduConnect.Tests
{
    public class OrgFollowTests : OrgTestBase
    {
        // ─── Schema ────────────────────────────

        [Fact]
        public void A_student_can_follow_an_org_only_once()
        {
            var s = Student();
            Follow(_org, s);
            _db.Context.ChangeTracker.Clear();

            Assert.ThrowsAny<DbUpdateException>(() => Follow(_org, s));
        }

        [Fact]
        public void Deleting_a_user_removes_their_follows()
        {
            var s = Student();
            Follow(_org, s);

            _db.Context.Users.Remove(s);
            _db.Context.SaveChanges();

            Assert.Empty(_db.NewContext().OrgFollowers);
        }

        // ─── Follow / Unfollow ─────────────────

        private int FollowRows(User u) =>
            _db.NewContext().OrgFollowers.Count(f => f.UserID == u.UserID);

        [Fact]
        public async Task Student_follows_and_unfollows()
        {
            var s = Student();

            Assert.IsType<RedirectToActionResult>(await Controller(s, RoleNames.Student).Follow(_org.OrgID, null));
            Assert.Equal(1, FollowRows(s));
            Assert.Empty(_db.NewContext().OrgMembers.Where(m => m.UserID == s.UserID));

            await Controller(s, RoleNames.Student).Unfollow(_org.OrgID, null);
            Assert.Equal(0, FollowRows(s));
        }

        [Fact]
        public async Task Following_twice_keeps_one_row()
        {
            var s = Student();
            await Controller(s, RoleNames.Student).Follow(_org.OrgID, null);
            await Controller(s, RoleNames.Student).Follow(_org.OrgID, null);

            Assert.Equal(1, FollowRows(s));
        }

        [Fact]
        public async Task Faculty_and_unverified_students_cannot_follow()
        {
            var unverified = Student(verified: false);

            var asFaculty = Controller(_faculty, RoleNames.Faculty);
            await asFaculty.Follow(_org.OrgID, null);
            Assert.NotNull(asFaculty.TempData["Error"]);

            var asUnverified = Controller(unverified, RoleNames.Student);
            await asUnverified.Follow(_org.OrgID, null);
            Assert.NotNull(asUnverified.TempData["Error"]);

            Assert.Empty(_db.NewContext().OrgFollowers);
        }

        [Fact]
        public async Task Cannot_follow_a_deactivated_or_missing_org()
        {
            var s = Student();
            _otherOrg.IsActive = false;
            _db.Context.SaveChanges();

            Assert.IsType<NotFoundResult>(await Controller(s, RoleNames.Student).Follow(_otherOrg.OrgID, null));
            Assert.IsType<NotFoundResult>(await Controller(s, RoleNames.Student).Follow(9999, null));
            Assert.Empty(_db.NewContext().OrgFollowers);
        }

        [Fact]
        public async Task Member_unfollow_is_a_noop_and_keeps_access()
        {
            var member = Student();
            Join(_org, member);

            Assert.IsType<RedirectToActionResult>(await Controller(member, RoleNames.Student).Unfollow(_org.OrgID, null));
            Assert.True(await Access().CanSeePostsAsync(member.UserID, RoleNames.Student, _org.OrgID));
        }

        [Fact]
        public async Task Students_from_any_college_can_follow()
        {
            var coe = _db.AddCollege("COE");
            var ce = _db.AddProgram(_db.AddDepartment(coe, "Civil"), "BSCE");
            var engineer = Student(programId: ce.ProgramID);
            _org.CollegeID = _db.Context.Departments.Find(_bsit.DepartmentID)!.CollegeID;
            _db.Context.SaveChanges();

            await Controller(engineer, RoleNames.Student).Follow(_org.OrgID, null);

            Assert.Equal(1, FollowRows(engineer));
        }

        [Fact]
        public async Task Follow_returns_to_a_local_url()
        {
            var s = Student();

            var result = await Controller(s, RoleNames.Student).Follow(_org.OrgID, "/Org");

            Assert.Equal("/Org", Assert.IsType<LocalRedirectResult>(result).Url);
        }

        [Fact]
        public async Task Not_logged_in_is_sent_to_login()
        {
            var controller = Controller(Student(), RoleNames.Student);
            _session.Clear();

            var result = Assert.IsType<RedirectToActionResult>(await controller.Follow(_org.OrgID, null));
            Assert.Equal("Login", result.ActionName);
        }

        // ─── Org page ──────────────────────────

        private OrgAnnouncement PostIn(Organization org, string title, DateTime? expires = null,
            DateTime? at = null)
        {
            var poster = Student("Po", "St");
            var a = new OrgAnnouncement
            {
                OrgID = org.OrgID, PostedByID = poster.UserID, Title = title, Body = "body",
                ExpiresAt = expires, PostedAt = at ?? DateTime.Now
            };
            _db.Context.OrgAnnouncements.Add(a);
            _db.Context.SaveChanges();
            return a;
        }

        private static List<OrgAnnouncement> Posts(ViewResult v) =>
            (List<OrgAnnouncement>)v.ViewData["Announcements"]!;

        [Fact]
        public async Task Non_follower_gets_about_us_and_no_posts()
        {
            PostIn(_org, "Cup sign-ups");
            var s = Student();

            var view = Assert.IsType<ViewResult>(await Controller(s, RoleNames.Student).Details(_org.OrgID));

            Assert.Empty(Posts(view));
            Assert.False((bool)view.ViewData["CanSeePosts"]!);
            Assert.Equal("None", view.ViewData["Relation"]);
        }

        [Fact]
        public async Task Follower_gets_posts()
        {
            PostIn(_org, "Cup sign-ups");
            var s = Student();
            Follow(_org, s);

            var view = Assert.IsType<ViewResult>(await Controller(s, RoleNames.Student).Details(_org.OrgID));

            Assert.Equal("Cup sign-ups", Assert.Single(Posts(view)).Title);
            Assert.True((bool)view.ViewData["CanSeePosts"]!);
            Assert.Equal("Following", view.ViewData["Relation"]);
            Assert.Equal(1, view.ViewData["FollowerCount"]);
        }

        [Fact]
        public async Task Member_relation_is_member_even_if_also_following()
        {
            var s = Student();
            Join(_org, s);
            Follow(_org, s);

            var view = Assert.IsType<ViewResult>(await Controller(s, RoleNames.Student).Details(_org.OrgID));

            Assert.Equal("Member", view.ViewData["Relation"]);
        }

        [Fact]
        public async Task Faculty_sees_about_us_without_follow_button()
        {
            PostIn(_org, "Cup sign-ups");

            var view = Assert.IsType<ViewResult>(await Controller(_faculty, RoleNames.Faculty).Details(_org.OrgID));

            Assert.Equal("NotStudent", view.ViewData["Relation"]);
            Assert.Empty(Posts(view));
        }

        // ─── /Org directory ────────────────────

        private async Task<OrgDirectoryViewModel> Directory(User u, string role) =>
            Assert.IsType<OrgDirectoryViewModel>(
                Assert.IsType<ViewResult>(await Controller(u, role).Index()).Model);

        [Fact]
        public async Task Directory_splits_followed_and_other_orgs()
        {
            var s = Student();
            Follow(_org, s);

            var vm = await Directory(s, RoleNames.Student);

            Assert.Equal(_org.OrgID, Assert.Single(vm.Following).Org.OrgID);
            Assert.Equal(OrgRelation.Following, vm.Following[0].Relation);
            Assert.Equal(_otherOrg.OrgID, Assert.Single(vm.Discover).Org.OrgID);
            Assert.True(vm.CanFollow);
        }

        [Fact]
        public async Task Discover_rows_never_leak_post_titles()
        {
            PostIn(_org, "Followed news");
            PostIn(_otherOrg, "Private news");
            var s = Student();
            Follow(_org, s);

            var vm = await Directory(s, RoleNames.Student);

            Assert.Equal("Followed news", vm.Following[0].LatestTitle);
            Assert.Null(vm.Discover[0].LatestTitle);
        }

        [Fact]
        public async Task Expired_posts_are_not_the_latest_title()
        {
            PostIn(_org, "Older live", at: DateTime.Now.AddDays(-2));
            PostIn(_org, "Newer expired", expires: DateTime.Now.AddDays(-1), at: DateTime.Now.AddHours(-1));
            var s = Student();
            Follow(_org, s);

            var vm = await Directory(s, RoleNames.Student);

            Assert.Equal("Older live", vm.Following[0].LatestTitle);
        }

        [Fact]
        public async Task Non_students_get_one_list_without_follow()
        {
            PostIn(_org, "News");

            var faculty = await Directory(_faculty, RoleNames.Faculty);
            Assert.False(faculty.CanFollow);
            Assert.Empty(faculty.Following);
            Assert.Equal(2, faculty.Discover.Count);
            Assert.All(faculty.Discover, r => Assert.Null(r.LatestTitle));

            var admin = await Directory(_admin, RoleNames.Administrator);
            Assert.False(admin.CanFollow);
            Assert.Equal("News", admin.Discover.Single(r => r.Org.OrgID == _org.OrgID).LatestTitle);
        }

        [Fact]
        public async Task Member_who_is_no_longer_eligible_still_finds_their_org()
        {
            PostIn(_org, "News");
            var s = Student();
            Join(_org, s);
            s.VerificationStatus = VerificationStatuses.Pending;
            _db.Context.SaveChanges();

            var vm = await Directory(s, RoleNames.Student);

            Assert.False(vm.CanFollow);
            Assert.Empty(vm.Following);
            var row = vm.Discover.Single(r => r.Org.OrgID == _org.OrgID);
            Assert.Equal(OrgRelation.Member, row.Relation);
            Assert.Equal("News", row.LatestTitle);
        }

        // ─── About Us ──────────────────────────

        private static OrgAboutUsViewModel About(string? text) => new() { AboutUs = text };

        private Organization ReloadOrg(Organization o) =>
            _db.NewContext().Organizations.Single(x => x.OrgID == o.OrgID);

        [Fact]
        public async Task Officer_edits_about_us_and_it_is_audited()
        {
            var officer = Student("Of", "Fi");
            Join(_org, officer, OrgRoles.Officer);
            _org.Description = "Old text";
            _db.Context.SaveChanges();

            var result = await Controller(officer, RoleNames.Student).AboutUs(_org.OrgID, About("New text"));

            Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("New text", ReloadOrg(_org).Description);
            var log = Assert.Single(_db.NewContext().AuditLogs);
            Assert.Equal("Edit About Us", log.Action);
            Assert.Equal(AuditArea.Organizations, log.TableAffected);
            Assert.Contains("Old text", log.OldValues);
            Assert.Contains("New text", log.NewValues);
        }

        [Fact]
        public async Task Admin_can_edit_about_us()
        {
            await AsAdmin().AboutUs(_org.OrgID, About("By admin"));

            Assert.Equal("By admin", ReloadOrg(_org).Description);
        }

        [Fact]
        public async Task Member_follower_and_other_org_officer_cannot_edit()
        {
            var member = Student("Me", "Mb");
            var follower = Student("Fo", "Ll");
            var otherOfficer = Student("Ot", "Of");
            Join(_org, member);
            Follow(_org, follower);
            Join(_otherOrg, otherOfficer, OrgRoles.Officer);

            foreach (var u in new[] { member, follower, otherOfficer })
            {
                Assert.IsType<RedirectToActionResult>(
                    await Controller(u, RoleNames.Student).AboutUs(_org.OrgID, About("Hijack")));
                Assert.IsType<RedirectToActionResult>(
                    await Controller(u, RoleNames.Student).AboutUs(_org.OrgID));
            }

            Assert.Null(ReloadOrg(_org).Description);
            Assert.Empty(_db.NewContext().AuditLogs);
        }

        [Fact]
        public async Task About_us_over_2000_chars_is_rejected()
        {
            var officer = Student();
            Join(_org, officer, OrgRoles.Officer);
            var vm = About(new string('a', 2001));
            var controller = Controller(officer, RoleNames.Student);
            var errors = new List<System.ComponentModel.DataAnnotations.ValidationResult>();
            System.ComponentModel.DataAnnotations.Validator.TryValidateObject(
                vm, new System.ComponentModel.DataAnnotations.ValidationContext(vm), errors, true);
            foreach (var e in errors)
                controller.ModelState.AddModelError(e.MemberNames.First(), e.ErrorMessage!);

            Assert.IsType<ViewResult>(await controller.AboutUs(_org.OrgID, vm));
            Assert.Null(ReloadOrg(_org).Description);
        }

        [Fact]
        public async Task About_us_line_breaks_count_once_toward_the_limit()
        {
            // The browser counts a line break as one character but submits
            // "\r\n"; text the counter shows as 2000 must save.
            var text = string.Join("\r\n", Enumerable.Repeat(new string('a', 99), 20)) + "a";
            Assert.Equal(2019, text.Length);
            Assert.Equal(2000, text.Replace("\r\n", "\n").Length);
            var vm = About(text);
            var controller = AsAdmin();
            var errors = new List<System.ComponentModel.DataAnnotations.ValidationResult>();
            System.ComponentModel.DataAnnotations.Validator.TryValidateObject(
                vm, new System.ComponentModel.DataAnnotations.ValidationContext(vm), errors, true);
            foreach (var e in errors)
                controller.ModelState.AddModelError(e.MemberNames.First(), e.ErrorMessage!);

            Assert.IsType<RedirectToActionResult>(await controller.AboutUs(_org.OrgID, vm));
            Assert.Equal(2000, ReloadOrg(_org).Description!.Length);
        }

        [Fact]
        public async Task About_us_html_is_stored_as_text()
        {
            await AsAdmin().AboutUs(_org.OrgID, About("<script>x</script>"));

            Assert.Equal("<script>x</script>", ReloadOrg(_org).Description);
        }

        [Fact]
        public async Task Officer_edit_cannot_change_name_logo_or_college()
        {
            var officer = Student();
            Join(_org, officer, OrgRoles.Officer);
            _org.LogoURL = "https://blob/logo.png";
            _db.Context.SaveChanges();

            await Controller(officer, RoleNames.Student).AboutUs(_org.OrgID,
                new OrgAboutUsViewModel { OrgName = "Renamed", AboutUs = "Hi" });

            var org = ReloadOrg(_org);
            Assert.Equal("Gaming Society", org.OrgName);
            Assert.Equal("https://blob/logo.png", org.LogoURL);
            Assert.Null(org.CollegeID);
        }

        // ─── Posting: photo, files, notifications ─

        private User Officer()
        {
            var o = Student("Of", "Fi");
            Join(_org, o, OrgRoles.Officer);
            return o;
        }

        [Fact]
        public async Task Post_with_photo_stores_photo_url()
        {
            var vm = APost();
            vm.Photo = Upload("poster.png", "image/png");

            Assert.IsType<RedirectToActionResult>(await Controller(Officer(), RoleNames.Student).Post(_org.OrgID, vm));

            var post = Assert.Single(_db.NewContext().OrgAnnouncements);
            Assert.NotNull(post.PhotoURL);
            Assert.Null(post.AttachmentURL);
        }

        [Fact]
        public async Task Non_image_photo_is_rejected()
        {
            var vm = APost();
            vm.Photo = Upload("x.pdf", "application/pdf");
            var controller = Controller(Officer(), RoleNames.Student);

            Assert.IsType<ViewResult>(await controller.Post(_org.OrgID, vm));
            Assert.True(controller.ModelState["Photo"]!.Errors.Count > 0);
            Assert.Empty(_db.NewContext().OrgAnnouncements);
        }

        [Fact]
        public async Task Image_as_file_attachment_is_rejected()
        {
            var vm = APost();
            vm.Attachment = Upload("p.jpg", "image/jpeg");
            var controller = Controller(Officer(), RoleNames.Student);

            Assert.IsType<ViewResult>(await controller.Post(_org.OrgID, vm));
            Assert.True(controller.ModelState["Attachment"]!.Errors.Count > 0);
            Assert.Empty(_db.NewContext().OrgAnnouncements);
        }

        [Fact]
        public async Task New_post_notifies_members_and_followers_once()
        {
            var officer = Officer();
            var member = Student("Me", "Mb");
            var follower = Student("Fo", "Ll");
            var both = Student("Bo", "Th");
            var unverifiedFollower = Student("Un", "Ve", verified: false);
            Join(_org, member);
            Follow(_org, follower);
            Join(_org, both);
            Follow(_org, both);
            Follow(_org, unverifiedFollower);
            Follow(_org, officer);

            await Controller(officer, RoleNames.Student).Post(_org.OrgID, APost());

            Assert.Equal(new[] { member.UserID, follower.UserID, both.UserID }.OrderBy(i => i),
                _notifications.Sent.Select(s => s.UserId).OrderBy(i => i));
        }

        [Fact]
        public async Task Follower_who_left_the_student_role_is_not_notified()
        {
            var officer = Officer();
            var former = Student("Fo", "Rm");
            Follow(_org, former);
            former.RoleID = _faculty.RoleID;
            _db.Context.SaveChanges();

            await Controller(officer, RoleNames.Student).Post(_org.OrgID, APost());

            Assert.Empty(_notifications.Sent);
        }

        // ─── Who sees posts ────────────────────

        private OrgAccess Access() => new(_db.NewContext());

        [Fact]
        public async Task Follower_member_and_admin_see_posts_others_do_not()
        {
            var follower = Student("Fe", "Lo");
            var member = Student("Me", "Mb");
            var officer = Student("Of", "Fi");
            var outsider = Student("Ou", "Ts");
            Follow(_org, follower);
            Join(_org, member);
            Join(_org, officer, OrgRoles.Officer);
            var access = Access();

            Assert.True(await access.CanSeePostsAsync(follower.UserID, RoleNames.Student, _org.OrgID));
            Assert.True(await access.CanSeePostsAsync(member.UserID, RoleNames.Student, _org.OrgID));
            Assert.True(await access.CanSeePostsAsync(officer.UserID, RoleNames.Student, _org.OrgID));
            Assert.True(await access.CanSeePostsAsync(_admin.UserID, RoleNames.Administrator, _org.OrgID));
            Assert.False(await access.CanSeePostsAsync(outsider.UserID, RoleNames.Student, _org.OrgID));
            Assert.False(await access.CanSeePostsAsync(_faculty.UserID, RoleNames.Faculty, _org.OrgID));
            Assert.False(await access.CanSeePostsAsync(follower.UserID, RoleNames.Student, _otherOrg.OrgID));
        }

        [Fact]
        public async Task Follower_who_is_deactivated_or_unverified_loses_access()
        {
            var deactivated = Student("De", "Ac");
            var unverified = Student("Un", "Ve");
            Follow(_org, deactivated);
            Follow(_org, unverified);
            deactivated.IsActive = false;
            unverified.VerificationStatus = VerificationStatuses.Pending;
            _db.Context.SaveChanges();
            var access = Access();

            Assert.False(await access.CanSeePostsAsync(deactivated.UserID, RoleNames.Student, _org.OrgID));
            Assert.False(await access.CanSeePostsAsync(unverified.UserID, RoleNames.Student, _org.OrgID));
        }

        [Fact]
        public async Task Retired_adviser_row_grants_nothing()
        {
            Join(_org, _faculty, OrgRoles.Adviser, active: false);

            Assert.False(await Access().CanSeePostsAsync(_faculty.UserID, RoleNames.Faculty, _org.OrgID));
        }

        [Fact]
        public async Task Nobody_sees_posts_of_a_deactivated_org()
        {
            var member = Student();
            Join(_org, member);
            _org.IsActive = false;
            _db.Context.SaveChanges();
            var access = Access();

            Assert.False(await access.CanSeePostsAsync(_admin.UserID, RoleNames.Administrator, _org.OrgID));
            Assert.False(await access.CanSeePostsAsync(member.UserID, RoleNames.Student, _org.OrgID));
        }

        [Fact]
        public async Task VisibleOrgIds_lists_followed_and_member_orgs_only()
        {
            var third = AddOrg("Chorale");
            var s = Student();
            Follow(_org, s);
            Join(_otherOrg, s);
            var access = Access();

            var mine = await access.VisibleOrgIds(s.UserID, RoleNames.Student).ToListAsync();
            var admins = await access.VisibleOrgIds(_admin.UserID, RoleNames.Administrator).ToListAsync();

            Assert.Equal(new[] { _org.OrgID, _otherOrg.OrgID }.OrderBy(i => i), mine.OrderBy(i => i));
            Assert.Equal(new[] { _org.OrgID, _otherOrg.OrgID, third.OrgID }.OrderBy(i => i), admins.OrderBy(i => i));
        }

        [Fact]
        public async Task PostAudience_is_members_and_followers_once_each()
        {
            var member = Student("Me", "Mb");
            var follower = Student("Fo", "Ll");
            var both = Student("Bo", "Th");
            var inactiveMember = Student("In", "Ac", active: false);
            var unverifiedFollower = Student("Un", "Ve");
            Join(_org, member);
            Follow(_org, follower);
            Join(_org, both);
            Follow(_org, both);
            Join(_org, inactiveMember);
            Follow(_org, unverifiedFollower);
            unverifiedFollower.VerificationStatus = VerificationStatuses.Pending;
            _db.Context.SaveChanges();

            var ids = await Access().PostAudience(_org.OrgID).ToListAsync();

            Assert.Equal(new[] { member.UserID, follower.UserID, both.UserID }.OrderBy(i => i), ids.OrderBy(i => i));
        }
    }
}
