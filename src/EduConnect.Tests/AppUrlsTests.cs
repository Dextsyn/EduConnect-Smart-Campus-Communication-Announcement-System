using EduConnect.Web;
using EduConnect.Web.Controllers;
using EduConnect.Web.Models;
using EduConnect.Web.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace EduConnect.Tests
{
    public class AppUrlsTests
    {
        private const string Production = "https://educonnect.example.edu";

        private static IConfiguration Config(string? baseUrl) =>
            new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["AppBaseUrl"] = baseUrl })
                .Build();

        private static HttpRequest Request(string scheme, string host)
        {
            var context = new DefaultHttpContext();
            context.Request.Scheme = scheme;
            context.Request.Host = new HostString(host);
            return context.Request;
        }

        [Fact]
        public void ConfiguredUrl_WinsOverTheRequestHost()
        {
            Assert.Equal(Production,
                AppUrls.BaseUrl(Config(Production + "/"), Request("http", "10.0.0.4")));
        }

        [Fact]
        public void NoSetting_FallsBackToTheRequestHost()
        {
            Assert.Equal(Production, AppUrls.BaseUrl(Config(""), Request("https", "educonnect.example.edu")));
            Assert.Equal(Production, AppUrls.BaseUrl(null, Request("https", "educonnect.example.edu")));
        }

        [Theory]
        [InlineData("https://localhost:7135")]
        [InlineData("http://127.0.0.1:5120")]
        [InlineData("https://educonnect.localhost")]
        public void LocalhostSetting_IsIgnoredOnAPublicRequest(string setting)
        {
            Assert.Equal(Production, AppUrls.BaseUrl(Config(setting), Request("https", "educonnect.example.edu")));
        }

        [Fact]
        public void LocalhostSetting_IsKeptWhenDevelopingLocally()
        {
            Assert.Equal("https://localhost:7135",
                AppUrls.BaseUrl(Config("https://localhost:7135"), Request("http", "localhost:5120")));
        }

        [Fact]
        public void NoRequest_UsesTheSetting()
        {
            Assert.Equal(Production, AppUrls.BaseUrl(Config(Production), null));
            Assert.Equal("", AppUrls.BaseUrl(Config(null), null));
        }

        [Fact]
        public void ShippedAppSettings_HaveNoLocalhostBaseUrl()
        {
            var webRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
                "..", "..", "..", "..", "EduConnect.Web"));
            var config = new ConfigurationBuilder()
                .AddJsonFile(Path.Combine(webRoot, "appsettings.json"))
                .Build();

            Assert.DoesNotContain("localhost", config["AppBaseUrl"] ?? "");
        }

        [Fact]
        public async Task ReviewerEmail_LinksToTheSiteTheRequestCameIn()
        {
            using var db = new TestDb();
            var mail = new FakeEmailService();
            var session = new FakeSession();
            var college = db.AddCollege("CCIT");
            var department = db.AddDepartment(college, "IT&IS");
            var faculty = db.AddUser(RoleNames.Faculty, college.CollegeID, department.DepartmentID);
            db.AddUser(RoleNames.Chairperson, college.CollegeID, department.DepartmentID);
            var post = db.AddAnnouncement(faculty, "For IT&IS");
            post.Status = "Draft";
            post.ApprovalStatus = "Draft";
            db.Context.SaveChanges();
            db.Target(post, d: department);

            session.SetString("UserID", faculty.UserID.ToString());
            session.SetString("RoleName", RoleNames.Faculty);
            var http = FakeSession.HttpContextWith(session);
            http.Request.Scheme = "https";
            http.Request.Host = new HostString("educonnect.example.edu");
            // The dev value a production box would have had before this fix.
            http.RequestServices = new ServiceCollection()
                .AddSingleton(Config("https://localhost:7135"))
                .BuildServiceProvider();

            var controller = new AnnouncementController(
                db.Context, NullLogger<AnnouncementController>.Instance, null!,
                new FakeNotificationService(), mail, new FakeBlobStorage(),
                new AudienceService(db.Context), new ApprovalService(db.Context))
            {
                ControllerContext = new ControllerContext { HttpContext = http },
                Url = new FakeUrlHelper()
            };
            controller.TempData = new TempDataDictionary(http, new NullTempDataProvider());

            await controller.Submit(post.AnnouncementID);

            var body = Assert.Single(mail.Bodies);
            Assert.Contains($"href='{Production}/Announcement/Review/{post.AnnouncementID}'", body);
            Assert.DoesNotContain("localhost", body);
        }
    }
}
