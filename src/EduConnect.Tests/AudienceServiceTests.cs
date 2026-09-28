using EduConnect.Web;
using EduConnect.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace EduConnect.Tests
{
    public class AudienceServiceTests : IDisposable
    {
        private readonly TestDb _db = new();

        public void Dispose() => _db.Dispose();

        [Fact]
        public void Target_WithTwoLevels_IsRejectedByTheDatabase()
        {
            var college = _db.AddCollege("CCIT");
            var dept = _db.AddDepartment(college, "IT&IS");
            var author = _db.AddUser(RoleNames.Dean, college.CollegeID);
            var a = _db.AddAnnouncement(author, "Two levels");

            Assert.Throws<DbUpdateException>(() => _db.Target(a, college, dept));
        }
    }
}
