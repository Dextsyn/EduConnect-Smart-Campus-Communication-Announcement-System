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
    }
}
