using EduConnect.Web.Data;
using EduConnect.Web.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Http.Connections.Features;
using Microsoft.AspNetCore.Session;
using Microsoft.AspNetCore.SignalR;
using System.Security.Claims;
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

    // A SignalR caller whose HttpContext carries the given session, for
    // driving hub methods directly.
    public sealed class FakeHubCallerContext : HubCallerContext
    {
        private readonly FeatureCollection _features = new();

        public FakeHubCallerContext(FakeSession session) =>
            _features.Set<IHttpContextFeature>(
                new HttpContextFeature { HttpContext = FakeSession.HttpContextWith(session) });

        private sealed class HttpContextFeature : IHttpContextFeature
        {
            public HttpContext? HttpContext { get; set; }
        }

        public override string ConnectionId => "test-connection";
        public override string? UserIdentifier => null;
        public override ClaimsPrincipal? User => null;
        public override IDictionary<object, object?> Items { get; } = new Dictionary<object, object?>();
        public override IFeatureCollection Features => _features;
        public override CancellationToken ConnectionAborted => CancellationToken.None;
        public override void Abort() { }
    }

    // Records what a hub broadcasts. Only Group(...) is used by the hubs
    // under test.
    public sealed class FakeHubClients : IHubCallerClients
    {
        public RecordingClientProxy Proxy { get; } = new();

        public sealed class RecordingClientProxy : IClientProxy
        {
            public List<(string Method, object?[] Args)> Calls { get; } = new();

            public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
            {
                Calls.Add((method, args));
                return Task.CompletedTask;
            }
        }

        public IClientProxy Group(string groupName) => Proxy;

        public IClientProxy Caller => throw new NotSupportedException();
        public IClientProxy Others => throw new NotSupportedException();
        public IClientProxy All => throw new NotSupportedException();
        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => throw new NotSupportedException();
        public IClientProxy Client(string connectionId) => throw new NotSupportedException();
        public IClientProxy Clients(IReadOnlyList<string> connectionIds) => throw new NotSupportedException();
        public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) => throw new NotSupportedException();
        public IClientProxy Groups(IReadOnlyList<string> groupNames) => throw new NotSupportedException();
        public IClientProxy OthersInGroup(string groupName) => throw new NotSupportedException();
        public IClientProxy User(string userId) => throw new NotSupportedException();
        public IClientProxy Users(IReadOnlyList<string> userIds) => throw new NotSupportedException();
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
