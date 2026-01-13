namespace Template.Logic.Interfaces
{
    public interface IFilePersistanceService
    {
        Task<BlobInfo?> Upload(Stream fileStream, string containerName, string blobName);

        Task<byte[]> Download(string fileName, string containerName);

        Task<BlobInfo?> GetBlobInfo(string fileName, string containerName);

        Task Delete(string fileName, string containerName);
    }
}