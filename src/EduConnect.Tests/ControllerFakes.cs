using EduConnect.Web.Data;
using EduConnect.Web.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Session;
using Microsoft.Extensions.Configuration;

namespace EduConnect.Tests
{
    // In-memory ISession for driving controllers that read the session.
    public sealed class FakeSession : ISession
    {
        private readonly Dictionary<string, byte[]> _values = new();

        public bool IsAvailable => true;
        public string Id => "test";
        public IEnumerable<string> Keys => _values.Keys;

        public void Clear() => _values.Clear();
        public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Remove(string key) => _values.Remove(key);
        public void Set(string key, byte[] value) => _values[key] = value;
        public bool TryGetValue(string key, out byte[] value) => _values.TryGetValue(key, out value!);

        public static DefaultHttpContext HttpContextWith(FakeSession session)
        {
            var context = new DefaultHttpContext();
            context.Features.Set<ISessionFeature>(new SessionFeature { Session = session });
            return context;
        }
    }

    // TempData that keeps nothing between requests.
    public sealed class NullTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
        public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
    }

    // Records notifications instead of saving or broadcasting them.
    public sealed class FakeNotificationService : INotificationService
    {
        public List<(int UserId, string Type)> Sent { get; } = new();

        public Task SendAsync(int userId, string type, string message, string? link = null, int? announcementId = null)
        {
            Sent.Add((userId, type));
            return Task.CompletedTask;
        }

        public Task SendToManyAsync(IEnumerable<int> userIds, string type, string message, string? link = null, int? announcementId = null)
        {
            foreach (var id in userIds) Sent.Add((id, type));
            return Task.CompletedTask;
        }

        public List<(int UserId, string EventName, object Payload)> Signals { get; } = new();

        public Task SignalAsync(IEnumerable<int> userIds, string eventName, object payload)
        {
            foreach (var id in userIds) Signals.Add((id, eventName, payload));
            return Task.CompletedTask;
        }
    }

    // A real AccountEmailService that mails through the given fake.
    public static class TestAccountEmails
    {
        public const string BaseUrl = "https://educonnect.test";

        public static AccountEmailService Create(ApplicationDbContext context, IEmailService email, HttpContext http) =>
            new(context, email,
                new ConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string?> { ["AppBaseUrl"] = BaseUrl })
                    .Build(),
                new HttpContextAccessor { HttpContext = http });
    }

    // Records addresses instead of sending mail.
    public sealed class FakeEmailService : IEmailService
    {
        public List<string> Sent { get; } = new();
        public List<string> Bodies { get; } = new();

        // Set to make every send fail, like an SMTP outage.
        public bool Fail { get; set; }

        public Task SendEmailAsync(string toEmail, string toName, string subject, string htmlBody)
        {
            if (Fail)
                throw new InvalidOperationException("SMTP unavailable");
            Sent.Add(toEmail);
            Bodies.Add(htmlBody);
            return Task.CompletedTask;
        }
    }

    // Records blob calls instead of talking to Azure.
    public sealed class FakeBlobStorage : IBlobStorageService
    {
        public List<string> Uploaded { get; } = new();
        public List<string> Deleted { get; } = new();

        public Task<string> UploadAsync(byte[] content, string fileName, string containerName, string contentType)
        {
            Uploaded.Add(fileName);
            return Task.FromResult($"https://blob.test/{containerName}/{fileName}");
        }

        public Task DeleteAsync(string blobName, string containerName)
        {
            Deleted.Add(blobName);
            return Task.CompletedTask;
        }
    }

    // Url.Action without routing: "/<action>".
    public sealed class FakeUrlHelper : IUrlHelper
    {
        public ActionContext ActionContext { get; } = new();
        public string? Action(UrlActionContext actionContext) => "/" + actionContext.Action;
        public string? Content(string? contentPath) => contentPath;
        public bool IsLocalUrl(string? url) => true;
        public string? Link(string? routeName, object? values) => null;
        public string? RouteUrl(UrlRouteContext routeContext) => null;
    }
}
