using EduConnect.Web.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Session;

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
}
