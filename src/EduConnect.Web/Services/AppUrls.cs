namespace EduConnect.Web.Services
{
    // The site's public address for links that leave the app: email buttons
    // and the event QR codes. Uses the AppBaseUrl setting (set it as an App
    // Service application setting in production), falling back to the host
    // the request came in on. A localhost AppBaseUrl only counts on a request
    // that itself came from localhost, so a dev setting can't leak into a
    // production email.
    public static class AppUrls
    {
        public const string SettingName = "AppBaseUrl";

        public static string BaseUrl(IConfiguration? config, HttpRequest? request)
        {
            var configured = config?[SettingName]?.Trim().TrimEnd('/');
            var fromRequest = request == null || !request.Host.HasValue
                ? null
                : $"{request.Scheme}://{request.Host}";

            if (!string.IsNullOrEmpty(configured) &&
                (fromRequest == null || !IsLoopback(configured) || IsLoopback(fromRequest)))
                return configured;

            return fromRequest ?? configured ?? "";
        }

        // Controllers that don't take IConfiguration read it from the request's
        // services; with none (unit tests) the request host is used.
        public static string BaseUrl(HttpContext context) =>
            BaseUrl(context.RequestServices?.GetService<IConfiguration>(), context.Request);

        private static bool IsLoopback(string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
                return false;
            return uri.IsLoopback ||
                uri.Host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase);
        }
    }
}
