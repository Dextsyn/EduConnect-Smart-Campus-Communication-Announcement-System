using System.Text.RegularExpressions;
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
    // Self-registration: email confirmation before the ITC queue.
    // Admin-created accounts: emailed temporary password, changed on first login.
    public class EmailVerificationTests : IDisposable
    {
        private readonly TestDb _db = new();
        private readonly FakeSession _session = new();
        private readonly FakeEmailService _email = new();
        private readonly FakeNotificationService _notifications = new();
        private readonly User _admin;
        private readonly AcademicProgram _bsit;

        public EmailVerificationTests()
        {
            _admin = _db.AddUser(RoleNames.Administrator);
            _db.Context.Roles.Add(new Role { RoleName = RoleNames.StudentPending, RoleLevel = 1 });
            _db.Context.Roles.Add(new Role { RoleName = RoleNames.Student, RoleLevel = 1 });
            _db.Context.SaveChanges();

            var college = _db.AddCollege("CCIT");
            var dept = _db.AddDepartment(college, "IT&IS");
            _bsit = _db.AddProgram(dept, "BSIT");
        }

        public void Dispose() => _db.Dispose();

        private AccountController Account()
        {
            var http = FakeSession.HttpContextWith(_session);
            var controller = new AccountController(
                _db.Context, NullLogger<AccountController>.Instance,
                null!, _email, _notifications, null!,
                new FakeBlobStorage(),
                new HierarchyService(_db.Context),
                new PlacementService(_db.Context),
                new AuditService(_db.Context, new HttpContextAccessor { HttpContext = http }),
                TestAccountEmails.Create(_db.Context, _email, http));
            controller.ControllerContext = new ControllerContext { HttpContext = http };
            controller.TempData = new TempDataDictionary(http, new NullTempDataProvider());
            return controller;
        }

        private AdminController Admin()
        {
            _session.SetString("UserID", _admin.UserID.ToString());
            _session.SetString("RoleName", RoleNames.Administrator);
            var http = FakeSession.HttpContextWith(_session);
            var controller = new AdminController(
                _db.Context, _email, NullLogger<AdminController>.Instance,
                new HierarchyService(_db.Context), new PlacementService(_db.Context),
                new AuditService(_db.Context, new HttpContextAccessor { HttpContext = http }),
                TestAccountEmails.Create(_db.Context, _email, http));
            controller.ControllerContext = new ControllerContext { HttpContext = http };
            controller.TempData = new TempDataDictionary(http, new NullTempDataProvider());
            return controller;
        }

        private RegisterViewModel Registration(string emailUser = "juan.delacruz") => new()
        {
            FirstName = "Juan",
            LastName = "Dela Cruz",
            EmailUser = emailUser,
            Password = "Secret123!",
            ConfirmPassword = "Secret123!",
            StudentID = "202312345",
            ProgramID = _bsit.ProgramID
        };

        private static string TokenFrom(string body) =>
            Uri.UnescapeDataString(Regex.Match(body, @"ConfirmEmail\?token=([^'""&<\s]+)").Groups[1].Value);

        // As a mail client shows it: the body is HTML, so "&" arrives as "&amp;".
        private static string PasswordFrom(string body) =>
            System.Net.WebUtility.HtmlDecode(
                Regex.Match(body, @"monospace;font-size:16px;'>\s*<strong>([^<]+)</strong>").Groups[1].Value);

        private Task<User> Saved(string email) =>
            _db.NewContext().Users.SingleAsync(u => u.Email == email);

        private const string StudentEmail = "juan.delacruz@adamson.edu.ph";

        // ─── Student registration ─────────────────

        [Fact]
        public async Task Register_AwaitsEmailConfirmation_AndDoesNotReachItc()
        {
            await Account().Register(Registration());

            var user = await Saved(StudentEmail);
            Assert.Equal(VerificationStatuses.EmailPending, user.VerificationStatus);
            Assert.False(user.IsActive);
            Assert.Equal(new[] { StudentEmail }, _email.Sent);
            Assert.NotEmpty(TokenFrom(_email.Bodies.Single()));
            Assert.Empty(_notifications.Sent);
        }

        [Fact]
        public async Task Register_StoresOnlyTheTokenHash()
        {
            await Account().Register(Registration());

            var token = TokenFrom(_email.Bodies.Single());
            var row = await _db.NewContext().EmailVerificationTokens.SingleAsync();
            Assert.NotEqual(token, row.TokenHash);
        }

        [Fact]
        public async Task ConfirmEmail_MovesToItcQueue_AndNotifiesAdmins()
        {
            await Account().Register(Registration());
            var token = TokenFrom(_email.Bodies.Single());

            await Account().ConfirmEmail(token);

            var user = await Saved(StudentEmail);
            Assert.Equal(VerificationStatuses.Pending, user.VerificationStatus);
            Assert.NotNull(user.EmailConfirmedAt);
            Assert.Contains((_admin.UserID, "NewPendingStudent"), _notifications.Sent);
        }

        [Fact]
        public async Task ConfirmEmail_TokenWorksOnlyOnce()
        {
            await Account().Register(Registration());
            var token = TokenFrom(_email.Bodies.Single());
            await Account().ConfirmEmail(token);

            var again = await Account().ConfirmEmail(token);

            Assert.Equal(nameof(AccountController.ResendVerification),
                Assert.IsType<RedirectToActionResult>(again).ActionName);
            Assert.Single(_notifications.Sent);
        }

        [Fact]
        public async Task ConfirmEmail_ExpiredToken_IsRefused()
        {
            await Account().Register(Registration());
            var token = TokenFrom(_email.Bodies.Single());
            var row = await _db.Context.EmailVerificationTokens.SingleAsync();
            row.ExpiresAt = DateTime.Now.AddMinutes(-1);
            await _db.Context.SaveChangesAsync();

            await Account().ConfirmEmail(token);

            Assert.Equal(VerificationStatuses.EmailPending, (await Saved(StudentEmail)).VerificationStatus);
        }

        [Fact]
        public async Task Login_BeforeConfirming_IsBlockedWithAResendOption()
        {
            await Account().Register(Registration());
            var controller = Account();

            var result = await controller.Login(new LoginViewModel { Email = StudentEmail, Password = "Secret123!" });

            Assert.IsType<ViewResult>(result);
            Assert.Equal(StudentEmail, controller.ViewBag.ResendVerificationEmail);
            Assert.Null(_session.GetString("UserID"));
        }

        [Fact]
        public async Task ResendVerification_ReplacesTheOldLink()
        {
            await Account().Register(Registration());
            var first = TokenFrom(_email.Bodies.Single());

            await Account().ResendVerification(new ResendVerificationViewModel { Email = StudentEmail });
            await Account().ConfirmEmail(first);

            Assert.Equal(2, _email.Sent.Count);
            Assert.Equal(VerificationStatuses.EmailPending, (await Saved(StudentEmail)).VerificationStatus);
        }

        [Fact]
        public async Task ResendVerification_UnknownEmail_SendsNothing()
        {
            await Account().ResendVerification(new ResendVerificationViewModel { Email = "nobody@adamson.edu.ph" });

            Assert.Empty(_email.Sent);
        }

        [Fact]
        public async Task Register_SameEmailWhileLinkIsLive_IsRefused()
        {
            await Account().Register(Registration());
            var controller = Account();

            var result = await controller.Register(Registration());

            Assert.IsType<ViewResult>(result);
            Assert.False(controller.ModelState.IsValid);
            Assert.Single(await _db.NewContext().Users.Where(u => u.Email == StudentEmail).ToListAsync());
        }

        [Fact]
        public async Task Register_OverAnExpiredUnconfirmedRegistration_StartsFresh()
        {
            await Account().Register(Registration());
            var stale = await Saved(StudentEmail);
            var row = await _db.Context.EmailVerificationTokens.SingleAsync();
            row.ExpiresAt = DateTime.Now.AddMinutes(-1);
            await _db.Context.SaveChangesAsync();

            await Account().Register(Registration());

            var user = await Saved(StudentEmail);
            Assert.NotEqual(stale.UserID, user.UserID);
            Assert.Equal(2, _email.Sent.Count);
        }

        [Fact]
        public async Task ApproveUser_UnconfirmedStudent_IsRefused()
        {
            await Account().Register(Registration());
            var user = await Saved(StudentEmail);

            await Admin().ApproveUser(user.UserID);

            Assert.Equal(VerificationStatuses.EmailPending, (await Saved(StudentEmail)).VerificationStatus);
        }

        // ─── Admin-created accounts ───────────────

        private async Task<(User User, string Password)> CreateStaffAccount()
        {
            var role = new Role { RoleName = RoleNames.Staff, RoleLevel = 1 };
            _db.Context.Roles.Add(role);
            await _db.Context.SaveChangesAsync();

            await Admin().AddUser(new AdminUserFormViewModel
            {
                FirstName = "Maria",
                LastName = "Santos",
                Email = "maria.santos@adamson.edu.ph",
                RoleID = role.RoleID,
                IsActive = true
            });
            _session.Clear();

            return (await Saved("maria.santos@adamson.edu.ph"), PasswordFrom(_email.Bodies.Last()));
        }

        [Fact]
        public async Task AddUser_EmailsATemporaryPasswordThatMustBeChanged()
        {
            var (user, password) = await CreateStaffAccount();

            Assert.Equal(12, password.Length);
            Assert.True(BCrypt.Net.BCrypt.Verify(password, user.PasswordHash));
            Assert.True(user.MustChangePassword);
            Assert.True(user.TempPasswordExpiresAt > DateTime.Now.AddHours(71));
            Assert.Equal(VerificationStatuses.Verified, user.VerificationStatus);

            var log = await _db.NewContext().AuditLogs.SingleAsync(a => a.Action == "Create");
            Assert.DoesNotContain(password, log.NewValues ?? "");
            Assert.DoesNotContain(password, log.Summary);
        }

        [Fact]
        public async Task AddUser_EmailFails_AccountExistsAndAdminIsTold()
        {
            _email.Fail = true;
            var role = new Role { RoleName = RoleNames.Staff, RoleLevel = 1 };
            _db.Context.Roles.Add(role);
            await _db.Context.SaveChangesAsync();
            var controller = Admin();

            await controller.AddUser(new AdminUserFormViewModel
            {
                FirstName = "Maria", LastName = "Santos",
                Email = "maria.santos@adamson.edu.ph", RoleID = role.RoleID, IsActive = true
            });

            Assert.NotNull(controller.TempData["Error"]);
            Assert.True((await Saved("maria.santos@adamson.edu.ph")).MustChangePassword);
        }

        [Fact]
        public async Task Login_WithTemporaryPassword_GoesToSetPassword()
        {
            var (user, password) = await CreateStaffAccount();

            var result = await Account().Login(new LoginViewModel { Email = user.Email, Password = password });

            Assert.Equal(nameof(AccountController.SetPassword),
                Assert.IsType<RedirectToActionResult>(result).ActionName);
            Assert.Equal("1", _session.GetString(PasswordChangeRequired.SessionKey));
            Assert.NotNull((await Saved(user.Email)).EmailConfirmedAt);
        }

        [Fact]
        public async Task Login_WithExpiredTemporaryPassword_IsRefused()
        {
            var (user, password) = await CreateStaffAccount();
            var tracked = await _db.Context.Users.SingleAsync(u => u.UserID == user.UserID);
            tracked.TempPasswordExpiresAt = DateTime.Now.AddMinutes(-1);
            await _db.Context.SaveChangesAsync();

            var result = await Account().Login(new LoginViewModel { Email = user.Email, Password = password });

            Assert.IsType<ViewResult>(result);
            Assert.Null(_session.GetString("UserID"));
        }

        [Fact]
        public async Task SetPassword_ReplacesTheTemporaryOne()
        {
            var (user, password) = await CreateStaffAccount();
            await Account().Login(new LoginViewModel { Email = user.Email, Password = password });

            await Account().SetPassword(new SetPasswordViewModel
            {
                NewPassword = "MyOwnPass1!",
                ConfirmPassword = "MyOwnPass1!"
            });

            var saved = await Saved(user.Email);
            Assert.False(saved.MustChangePassword);
            Assert.Null(saved.TempPasswordExpiresAt);
            Assert.True(BCrypt.Net.BCrypt.Verify("MyOwnPass1!", saved.PasswordHash));
            Assert.Null(_session.GetString(PasswordChangeRequired.SessionKey));
        }

        [Fact]
        public async Task SetPassword_SameAsTemporary_IsRefused()
        {
            var (user, password) = await CreateStaffAccount();
            await Account().Login(new LoginViewModel { Email = user.Email, Password = password });

            var result = await Account().SetPassword(new SetPasswordViewModel
            {
                NewPassword = password,
                ConfirmPassword = password
            });

            Assert.IsType<ViewResult>(result);
            Assert.True((await Saved(user.Email)).MustChangePassword);
        }

        [Fact]
        public async Task ResendCredentials_OldPasswordStopsWorking()
        {
            var (user, oldPassword) = await CreateStaffAccount();

            await Admin().ResendCredentials(user.UserID);

            var newPassword = PasswordFrom(_email.Bodies.Last());
            var saved = await Saved(user.Email);
            Assert.NotEqual(oldPassword, newPassword);
            Assert.False(BCrypt.Net.BCrypt.Verify(oldPassword, saved.PasswordHash));
            Assert.True(BCrypt.Net.BCrypt.Verify(newPassword, saved.PasswordHash));
            Assert.Contains(await _db.NewContext().AuditLogs.ToListAsync(),
                a => a.Action == "ResendCredentials" && !a.Summary.Contains(newPassword));
        }

        [Fact]
        public async Task ResendCredentials_EmailFails_KeepsTheOldPassword()
        {
            var (user, oldPassword) = await CreateStaffAccount();
            _email.Fail = true;

            await Admin().ResendCredentials(user.UserID);

            Assert.True(BCrypt.Net.BCrypt.Verify(oldPassword, (await Saved(user.Email)).PasswordHash));
        }
    }

    public class PasswordChangeRequiredTests
    {
        private const string Html = "text/html,application/xhtml+xml,*/*;q=0.8";

        [Fact]
        public void FlaggedPageLoad_Redirects() =>
            Assert.True(PasswordChangeRequired.ShouldRedirect("1", "GET", "/Home/Index", Html));

        [Fact]
        public void NotFlagged_DoesNotRedirect() =>
            Assert.False(PasswordChangeRequired.ShouldRedirect(null, "GET", "/Home/Index", Html));

        [Fact]
        public void Post_DoesNotRedirect() =>
            Assert.False(PasswordChangeRequired.ShouldRedirect("1", "POST", "/Home/Index", Html));

        [Fact]
        public void Fetch_DoesNotRedirect() =>
            Assert.False(PasswordChangeRequired.ShouldRedirect("1", "GET", "/Notification/UnreadCount", "*/*"));

        [Theory]
        [InlineData("/Account/SetPassword")]
        [InlineData("/Account/Logout")]
        [InlineData("/Home/Error")]
        public void AllowedPaths_DoNotRedirect(string path) =>
            Assert.False(PasswordChangeRequired.ShouldRedirect("1", "GET", path, Html));

        [Fact]
        public void ProgramCompletion_LetsSetPasswordThrough() =>
            Assert.False(ProgramCompletion.ShouldRedirect("1", "GET", PasswordChangeRequired.SetPasswordPath, Html));
    }
}
