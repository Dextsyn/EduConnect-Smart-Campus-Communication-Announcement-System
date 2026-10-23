using EduConnect.Web;
using EduConnect.Web.Models;
using EduConnect.Web.Services;
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
