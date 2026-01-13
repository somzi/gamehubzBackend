using Microsoft.Extensions.Configuration;

namespace GameHubz.Logic.Services
{
    public class FileSystemService : IFilePersistanceService
    {
        private readonly string rootFolder;

        public FileSystemService(IConfiguration configuration)
        {
            this.rootFolder = configuration.GetStringThrowIfNull("RootFilesFolder");
        }

        public Task Delete(string fileName, string containerName)
        {
            if (this.CheckIfFileExists(fileName, containerName))
            {
                string path = this.GetFilePath(fileName, containerName);

                File.Delete(path);
            }

            return Task.CompletedTask;
        }

        public async Task<byte[]> Download(string fileName, string containerName)
        {
            if (this.CheckIfFileExists(fileName, containerName, true))
            {
                string path = this.GetFilePath(fileName, containerName);

                byte[] bytes = await File.ReadAllBytesAsync(path);

                return bytes;
            }

            // It will never execute
#pragma warning disable CS8603 // Possible null reference return.
            return null;
#pragma warning restore CS8603 // Possible null reference return.
        }

        public Task<BlobInfo?> GetBlobInfo(string fileName, string containerName)
        {
            var blobInfo = new BlobInfo()
            {
                Name = fileName,
                Url = "", // TODO: set URL
            };

            return Task.FromResult<BlobInfo?>(blobInfo);
        }

        public async Task<BlobInfo?> Upload(Stream fileStream, string containerName, string blobName)
        {
            using var memoryStream = new MemoryStream();

            fileStream.CopyTo(memoryStream);
            byte[] bytes = memoryStream.ToArray();

            string path = this.GetFilePath(blobName, containerName);

            await File.WriteAllBytesAsync(path, bytes);

            return await this.GetBlobInfo(blobName, containerName);
        }

        private bool CheckIfFileExists(
            string fileName,
            string containerName,
            bool doThrowException = false)
        {
            string path = this.GetFilePath(fileName, containerName);

            bool fileExists = File.Exists(path);

            if (!fileExists && doThrowException)
            {
                throw new Exception("File doesn't exist");
            }

            return fileExists;
        }

        private string GetFilePath(
            string fileName,
            string containerName)
        {
            return Path.Combine(
                rootFolder,
                GetFolderByContainerName(containerName),
                fileName);
        }

        private string GetFolderByContainerName(string containerName)
        {
            // TODO: get folder name by containerName
            return "";
        }
    }
}
