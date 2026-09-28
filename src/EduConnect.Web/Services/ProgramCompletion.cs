namespace EduConnect.Web.Services
{
    // A verified student with no program must choose one before using the
    // app. Login sets a session flag; a middleware sends their page loads
    // to the profile until it is cleared. Background fetches, SignalR and
    // form posts pass through untouched.
    public static class ProgramCompletion
    {
        public const string SessionKey = "NeedsProgram";
        public const string ProfilePath = "/Account/Profile";

        private static readonly string[] AllowedPaths =
        {
            "/Account/Profile",
            "/Account/ChangePassword",
            "/Account/Logout",
            "/Account/Login",
            // The exception handler re-runs failed requests here; redirecting
            // it would turn an error on Profile into a redirect loop.
            "/Home/Error"
        };

        public static bool NeedsProgram(string? roleName, int? programId) =>
            roleName == RoleNames.Student && programId == null;

        public static bool ShouldRedirect(string? sessionFlag, string method, string path, string? accept) =>
            sessionFlag == "1" &&
            string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase) &&
            accept != null && accept.Contains("text/html", StringComparison.OrdinalIgnoreCase) &&
            !AllowedPaths.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase));
    }
}
