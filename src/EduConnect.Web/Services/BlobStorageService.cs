using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace EduConnect.Web.Services
{
    public interface IBlobStorageService
    {
        // Uploads the given bytes to the target container and
        // returns the blob's public URL.
        Task<string> UploadAsync(
            byte[] content,
            string fileName,
            string containerName,
            string contentType);
    }

    public class BlobStorageService : IBlobStorageService
    {
        private readonly IConfiguration _config;
        private readonly ILogger<BlobStorageService> _logger;

        public BlobStorageService(
            IConfiguration config,
            ILogger<BlobStorageService> logger)
        {
            _config = config;
            _logger = logger;
        }

        public async Task<string> UploadAsync(
            byte[] content,
            string fileName,
            string containerName,
            string contentType)
        {
            var connectionString =
                _config["AzureBlobStorage"];

            if (string.IsNullOrWhiteSpace(connectionString))
                throw new InvalidOperationException(
                    "Azure Blob Storage connection string is not " +
                    "configured. Set the 'AzureBlobStorage' app " +
                    "setting.");

            var containerClient = new BlobContainerClient(
                connectionString, containerName);

            var blobClient =
                containerClient.GetBlobClient(fileName);

            using var stream = new MemoryStream(content);

            await blobClient.UploadAsync(
                stream,
                new BlobUploadOptions
                {
                    HttpHeaders = new BlobHttpHeaders
                    {
                        ContentType = contentType
                    }
                });

            return blobClient.Uri.ToString();
        }
    }
}
