using EduConnect.Web;
using EduConnect.Web.Controllers;
using EduConnect.Web.Services;
using EduConnect.Web.ViewModels;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Logging.Abstractions;

namespace EduConnect.Tests
{
    // Manage Users: 15 per page, numbered pager, filters carried into page
    // links, and the partial the as-you-type search swaps in.
    public class UserListTests : IDisposable
    {
        private readonly TestDb _db = new();
        private readonly FakeSession _session = new();

        public UserListTests()
        {
            var admin = _db.AddUser(RoleNames.Administrator);
            _session.SetString("UserID", admin.UserID.ToString());
        }

        public void Dispose() => _db.Dispose();

        private AdminController Controller()
        {
            _session.SetString("RoleName", RoleNames.Administrator);
            var http = FakeSession.HttpContextWith(_session);
            var email = new FakeEmailService();
            var controller = new AdminController(
                _db.Context, email, NullLogger<AdminController>.Instance,
                new HierarchyService(_db.Context), new PlacementService(_db.Context),
                new AuditService(_db.Context, new HttpContextAccessor { HttpContext = http }),
                TestAccountEmails.Create(_db.Context, email, http));
            controller.ControllerContext = new ControllerContext { HttpContext = http };
            controller.TempData = new TempDataDictionary(http, new NullTempDataProvider());
            return controller;
        }

        private static UserListViewModel Paged(int total, int page) =>
            new() { TotalCount = total, Page = page };

        [Theory]
        [InlineData(0, 1, 1, 0, 0)]
        [InlineData(15, 1, 1, 1, 15)]
        [InlineData(16, 2, 2, 16, 16)]
        [InlineData(212, 2, 15, 16, 30)]
        [InlineData(32, 3, 3, 31, 32)]
        public void PagingMath(int total, int page, int pageCount, int first, int last)
        {
            var model = Paged(total, page);

            Assert.Equal(pageCount, model.PageCount);
            Assert.Equal(first, model.FirstShown);
            Assert.Equal(last, model.LastShown);
        }

        [Theory]
        [InlineData(0, 1)]
        [InlineData(-3, 1)]
        [InlineData(2, 2)]
        [InlineData(99, 3)]
        public void ClampedPage_StaysInRange(int asked, int expected) =>
            Assert.Equal(expected, Paged(32, 1).ClampedPage(asked));

        [Fact]
        public void PageNumbers_ShowEndsAndNeighbours_WithGaps()
        {
            Assert.Equal(new int?[] { 1, null, 5, 6, 7, 8, 9, null, 14 }, Paged(210, 7).PageNumbers());
            Assert.Equal(new int?[] { 1, 2, 3, null, 14 }, Paged(210, 1).PageNumbers());
            Assert.Equal(new int?[] { 1, 2, 3, 4, 5, 6, null, 14 }, Paged(210, 4).PageNumbers());
            Assert.Equal(new int?[] { 1 }, Paged(5, 1).PageNumbers());
        }

        [Fact]
        public void RouteFor_CarriesEveryFilterThatIsSet()
        {
            var model = new UserListViewModel
            {
                SearchQuery = "dela cruz & co",
                FilterRole = RoleNames.Student,
                FilterStatus = "Verified",
                FilterCollege = 4,
                FilterPlacement = "missing"
            };

            Assert.Equal(new Dictionary<string, string>
            {
                ["searchQuery"] = "dela cruz & co",
                ["filterRole"] = RoleNames.Student,
                ["filterStatus"] = "Verified",
                ["filterCollege"] = "4",
                ["filterPlacement"] = "missing",
                ["page"] = "3"
            }, model.RouteFor(3));
        }

        [Fact]
        public void RouteFor_LeavesOutEmptyFilters()
        {
            var model = new UserListViewModel { SearchQuery = "", FilterRole = null };

            Assert.Equal(new Dictionary<string, string> { ["page"] = "2" }, model.RouteFor(2));
        }

        [Fact]
        public async Task Users_ShowsFifteenPerPage_AndClampsPastTheEnd()
        {
            for (var i = 0; i < 31; i++) _db.AddUser(RoleNames.Student); // 32 with the admin

            var second = (UserListViewModel)((ViewResult)await Controller().Users(
                null, null, null, null, page: 2)).Model!;
            var pastEnd = (UserListViewModel)((ViewResult)await Controller().Users(
                null, null, null, null, page: 99)).Model!;

            Assert.Equal(32, second.TotalCount);
            Assert.Equal(15, second.Users.Count);
            Assert.Equal(3, pastEnd.Page);
            Assert.Equal(2, pastEnd.Users.Count);
        }

        [Fact]
        public async Task Users_PagesDoNotOverlap()
        {
            for (var i = 0; i < 20; i++) _db.AddUser(RoleNames.Student);

            var first = (UserListViewModel)((ViewResult)await Controller().Users(
                null, null, null, null, page: 1)).Model!;
            var second = (UserListViewModel)((ViewResult)await Controller().Users(
                null, null, null, null, page: 2)).Model!;

            var ids = first.Users.Concat(second.Users).Select(u => u.UserID).ToList();
            Assert.Equal(21, ids.Distinct().Count());
        }

        [Fact]
        public async Task Users_Partial_ReturnsJustTheTable_WithFiltersApplied()
        {
            var match = _db.AddUser(RoleNames.Student);
            match.FirstName = "Zorbak";
            _db.AddUser(RoleNames.Faculty).FirstName = "Zorbak";
            _db.AddUser(RoleNames.Student);
            _db.Context.SaveChanges();

            // SQLite matches case-sensitively; SQL Server, as deployed, does not.
            var result = await Controller().Users(
                "  Zorb ", RoleNames.Student, null, null, page: 5, partial: true);

            var partial = Assert.IsType<PartialViewResult>(result);
            Assert.Equal("_UsersTable", partial.ViewName);
            var model = (UserListViewModel)partial.Model!;
            Assert.Equal("Zorb", model.SearchQuery);
            Assert.Equal(1, model.Page);
            Assert.Equal(new[] { match.UserID }, model.Users.Select(u => u.UserID));
        }
    }
}
