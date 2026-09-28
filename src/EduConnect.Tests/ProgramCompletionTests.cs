using EduConnect.Web;
using EduConnect.Web.Services;

namespace EduConnect.Tests
{
    public class ProgramCompletionTests
    {
        private const string Html = "text/html,application/xhtml+xml,*/*;q=0.8";

        [Fact]
        public void NeedsProgram_StudentWithoutProgram_True() =>
            Assert.True(ProgramCompletion.NeedsProgram(RoleNames.Student, null));

        [Fact]
        public void NeedsProgram_StudentWithProgram_False() =>
            Assert.False(ProgramCompletion.NeedsProgram(RoleNames.Student, 5));

        [Theory]
        [InlineData(RoleNames.Faculty)]
        [InlineData(RoleNames.Dean)]
        [InlineData(RoleNames.Administrator)]
        [InlineData(null)]
        public void NeedsProgram_OtherRoles_False(string? role) =>
            Assert.False(ProgramCompletion.NeedsProgram(role, null));

        [Fact]
        public void ShouldRedirect_FlaggedPageLoad_True() =>
            Assert.True(ProgramCompletion.ShouldRedirect("1", "GET", "/Home/Index", Html));

        [Fact]
        public void ShouldRedirect_NotFlagged_False() =>
            Assert.False(ProgramCompletion.ShouldRedirect(null, "GET", "/Home/Index", Html));

        [Fact]
        public void ShouldRedirect_FetchWithoutHtmlAccept_False() =>
            Assert.False(ProgramCompletion.ShouldRedirect("1", "GET", "/Notification/UnreadCount", "*/*"));

        [Fact]
        public void ShouldRedirect_Post_False() =>
            Assert.False(ProgramCompletion.ShouldRedirect("1", "POST", "/Home/Index", Html));

        [Theory]
        [InlineData("/Account/Profile")]
        [InlineData("/account/profile")]
        [InlineData("/Account/ChangePassword")]
        [InlineData("/Account/Logout")]
        [InlineData("/Account/Login")]
        [InlineData("/Home/Error")]
        public void ShouldRedirect_AllowedPages_False(string path) =>
            Assert.False(ProgramCompletion.ShouldRedirect("1", "GET", path, Html));
    }
}
