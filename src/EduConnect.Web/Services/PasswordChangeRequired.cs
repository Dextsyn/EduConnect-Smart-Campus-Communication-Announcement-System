namespace EduConnect.Web.Services
{
    // A user logged in with a system-issued temporary password must choose
    // their own before using the app. Login sets a session flag; a
    // middleware sends their page loads to SetPassword until it is cleared.
    // Background fetches, SignalR and form posts pass through untouched.
    public static class PasswordChangeRequired
    {
        public const string SessionKey = "MustChangePassword";
        public const string SetPasswordPath = "/Account/SetPassword";

        private static readonly string[] AllowedPaths =
        {
            SetPasswordPath,
            "/Account/Logout",
            "/Account/Login",
            // The exception handler re-runs failed requests here; redirecting
            // it would turn an error on SetPassword into a redirect loop.
            "/Home/Error"
        };

        public static bool ShouldRedirect(string? sessionFlag, string method, string path, string? accept) =>
            sessionFlag == "1" &&
            string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase) &&
            accept != null && accept.Contains("text/html", StringComparison.OrdinalIgnoreCase) &&
            !AllowedPaths.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase));
    }
}
