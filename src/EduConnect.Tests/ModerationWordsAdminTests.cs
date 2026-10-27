using EduConnect.Web;
using EduConnect.Web.Controllers;
using EduConnect.Web.Models;
using EduConnect.Web.Services;
using EduConnect.Web.ViewModels;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Caching.Memory;

namespace EduConnect.Tests
{
    // Admin → Moderation Words: the list behind Group Finder moderation.
    // Words here are made up.
    public class ModerationWordsAdminTests : IDisposable
    {
        private readonly TestDb _db = new();
        private readonly MemoryCache _cache = new(new MemoryCacheOptions());
        private readonly FakeSession _session = new();
        private readonly ContentModerationService _moderation;

        public ModerationWordsAdminTests()
        {
            var admin = _db.AddUser(RoleNames.Administrator);
            _session.SetString("UserID", admin.UserID.ToString());
            _session.SetString("UserName", "Ada Admin");
            _session.SetString("RoleName", RoleNames.Administrator);
            _moderation = new ContentModerationService(_db.Context, _cache);
        }

        public void Dispose()
        {
            _cache.Dispose();
            _db.Dispose();
        }

        private ModerationWordsController Controller()
        {
            var http = FakeSession.HttpContextWith(_session);
            var audit = new AuditService(_db.Context, new HttpContextAccessor { HttpContext = http });
            return new ModerationWordsController(_db.Context, _moderation, audit)
            {
                ControllerContext = new ControllerContext { HttpContext = http },
                TempData = new TempDataDictionary(http, new NullTempDataProvider())
            };
        }

        private ModerationWord AddWord(string word)
        {
            var row = new ModerationWord { Word = word, Language = ModerationLanguages.English };
            _db.Context.ModerationWords.Add(row);
            _db.Context.SaveChanges();
            return row;
        }

        [Fact]
        public async Task NonAdmin_IsRefused()
        {
            var student = _db.AddUser(RoleNames.Student);
            _session.SetString("UserID", student.UserID.ToString());
            _session.SetString("RoleName", RoleNames.Student);
            var row = AddWord("zorbak");

            Assert.IsType<ForbidResult>(await Controller().Index());
            Assert.IsType<ForbidResult>(await Controller().Add("flimbo", "English", false));
            Assert.IsType<ForbidResult>(await Controller().Remove(row.ModerationWordID));
            Assert.Single(_db.NewContext().ModerationWords);
        }

        [Fact]
        public async Task LoggedOut_GoesToLogin()
        {
            _session.Clear();

            var redirect = Assert.IsType<RedirectToActionResult>(await Controller().Index());
            Assert.Equal("Login", redirect.ActionName);
        }

        [Fact]
        public async Task Index_ListsBlockedAndAllowedSorted()
        {
            AddWord("zorbak");
            AddWord("flimbo");
            _db.Context.ModerationWords.Add(new ModerationWord { Word = "zorbaak", IsAllowed = true });
            _db.Context.SaveChanges();

            var view = Assert.IsType<ViewResult>(await Controller().Index());
            var model = Assert.IsType<ModerationWordsViewModel>(view.Model);
            Assert.Equal(new[] { "flimbo", "zorbak" }, model.Prohibited.Select(w => w.Word));
            Assert.Equal(new[] { "zorbaak" }, model.Allowed.Select(w => w.Word));
        }

        [Fact]
        public async Task Add_NormalizesAndSaves()
        {
            var result = await Controller().Add("  ZorBak ", "Tagalog", false);

            var redirect = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("Index", redirect.ActionName);
            var ctx = _db.NewContext();
            var row = Assert.Single(ctx.ModerationWords);
            Assert.Equal("zorbak", row.Word);
            Assert.Equal("Tagalog", row.Language);
            Assert.False(row.IsAllowed);
            var log = Assert.Single(ctx.AuditLogs);
            Assert.Equal("Add", log.Action);
            Assert.Equal(AuditArea.Moderation, log.TableAffected);
        }

        [Theory]
        [InlineData("   ", "English", "Enter a word.")]
        [InlineData("zorbak", "English", "That word is already on the list.")]
        [InlineData("flimbo", "French", "Choose English, Tagalog or Taglish.")]
        [InlineData("flimbo", null, "Choose English, Tagalog or Taglish.")]
        public async Task Add_RejectsBlankDuplicateAndBadLanguage(string word, string? language, string error)
        {
            AddWord("zorbak");
            var controller = Controller();

            await controller.Add(word, language, false);

            Assert.Equal(error, controller.TempData["Error"]);
            Assert.Single(_db.NewContext().ModerationWords);
            Assert.Empty(_db.NewContext().AuditLogs);
        }

        [Fact]
        public async Task Add_TooLong_IsRejected()
        {
            var controller = Controller();

            await controller.Add(new string('z', 101), "English", false);

            Assert.Equal("Keep it to 100 characters or fewer.", controller.TempData["Error"]);
            Assert.Empty(_db.NewContext().ModerationWords);
        }

        [Fact]
        public async Task Add_IsCaughtOnNextMessage()
        {
            Assert.Equal("flimbo", await _moderation.MaskAsync("flimbo"));

            await Controller().Add("flimbo", "English", false);

            Assert.Equal("******", await _moderation.MaskAsync("flimbo"));
        }

        [Fact]
        public async Task Remove_DeletesAuditsAndInvalidates()
        {
            var row = AddWord("zorbak");
            Assert.Equal("******", await _moderation.MaskAsync("zorbak"));

            var result = await Controller().Remove(row.ModerationWordID);

            Assert.IsType<RedirectToActionResult>(result);
            var ctx = _db.NewContext();
            Assert.Empty(ctx.ModerationWords);
            Assert.Equal("Remove", Assert.Single(ctx.AuditLogs).Action);
            Assert.Equal("zorbak", await _moderation.MaskAsync("zorbak"));
        }

        [Fact]
        public async Task Remove_UnknownId_IsNotFound()
        {
            Assert.IsType<NotFoundResult>(await Controller().Remove(999));
        }
    }
}
