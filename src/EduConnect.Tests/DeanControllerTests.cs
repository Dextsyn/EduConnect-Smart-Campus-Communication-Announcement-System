using EduConnect.Web;
using EduConnect.Web.Controllers;
using EduConnect.Web.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;

namespace EduConnect.Tests
{
    public class DeanControllerTests : IDisposable
    {
        private readonly TestDb _db = new();

        public void Dispose() => _db.Dispose();

        [Fact]
        public async Task Index_AccountDeletedWhileLoggedIn_EndsTheSession()
        {
            var session = new FakeSession();
            session.SetString("UserID", "999");
            session.SetString("RoleName", RoleNames.Dean);
            var controller = new DeanController(_db.Context, NullLogger<DeanController>.Instance, new AudienceService(_db.Context))
            {
                ControllerContext = new ControllerContext { HttpContext = FakeSession.HttpContextWith(session) }
            };

            var result = await controller.Index();

            Assert.IsType<RedirectToActionResult>(result);
            Assert.Empty(session.Keys);
        }
    }
}
