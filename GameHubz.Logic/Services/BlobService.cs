using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using GameHubz.DataModels.Config;
using BlobInfo = GameHubz.DataModels.Models.BlobInfo;

namespace GameHubz.Logic.Services
{
    public class BlobService : IFilePersistanceService
    {
        private readonly BlobConfig blobConfig;
        private readonly ILogger<BlobService> logger;

        public BlobService(IOptions<BlobConfig> config, ILogger<BlobService> logger)
        {
            this.blobConfig = config.Value;
            this.logger = logger;
        }

        public async Task<BlobInfo?> Upload(Stream fileStream, string containerName, string blobName)
        {
            BlobServiceClient serviceClient = new(this.blobConfig.BlobConnectionString);
            BlobContainerClient containerClient = serviceClient.GetBlobContainerClient(containerName);
            await containerClient.CreateIfNotExistsAsync(PublicAccessType.BlobContainer);

            BlobClient blobClient = containerClient.GetBlobClient(blobName);
            await blobClient.UploadAsync(fileStream);

            BlobInfo blobInfo = new()
            {
                Name = blobClient.Name,
                Url = blobClient.Uri.AbsoluteUri
            };

            return blobInfo;
        }

        public async Task<byte[]> Download(string fileName, string containerName)
        {
            BlobClient blob = new(this.blobConfig.BlobConnectionString, containerName, fileName);

            bool blobExists = await blob.ExistsAsync();
            if (!blobExists)
            {
                throw new IOException("Specified Blob does not exist");
            }

            using MemoryStream stream = new();
            await blob.DownloadToAsync(stream);

            return stream.ToArray();
        }

        public async Task<BlobInfo?> GetBlobInfo(string fileName, string containerName)
        {
            BlobClient blobClient = new(this.blobConfig.BlobConnectionString, containerName, fileName);

            bool blobExists = false;

            try
            {
                blobExists = await blobClient.ExistsAsync();
            }
            catch (AggregateException e)
            {
                this.logger.LogError($"Error while checking if blob exists in storage: {e}");

                if (e.InnerException != null && e.InnerException is Azure.RequestFailedException)
                {
                    this.logger.LogError($"Error connecting to storage: {e.InnerException}");
                }
            }

            if (!blobExists)
            {
                this.logger.LogError($"Blob not found. Filename '{fileName ?? ""}', Container: '{containerName}'.");
                return null;
            }

            BlobInfo blobInfo = new()
            {
                Name = blobClient.Name,
                Url = blobClient.Uri.AbsoluteUri,
            };

            return blobInfo;
        }

        public async Task Delete(string fileName, string containerName)
        {
            BlobClient blob = new(this.blobConfig.BlobConnectionString, containerName, fileName);

            bool blobExists = await blob.ExistsAsync();
            if (!blobExists)
            {
                throw new IOException("Specified Blob does not exist");
            }

            await blob.DeleteAsync();
        }
    }
}
