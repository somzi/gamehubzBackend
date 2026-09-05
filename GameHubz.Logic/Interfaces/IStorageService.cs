using GameHubz.DataModels.Enums;
using Microsoft.AspNetCore.Http;

namespace GameHubz.Logic.Interfaces
{
    /// <summary>What an upload leaves behind: the URL clients read, and the handle deletes need.</summary>
    public record StoredAsset(string Url, string StorageKey, StorageProviderType Provider);

    /// <summary>
    /// The seam between "we hold a file" and "Cloudinary holds a file". It exists for one concrete
    /// reason: video is a different cost animal from images (storage accrues, bandwidth is billed
    /// per view) and will likely end up on plain object storage, while images stay where they are.
    /// Keeping uploads and deletes behind this interface means that becomes one new class plus a
    /// config switch rather than a rewrite of every call site.
    /// </summary>
    public interface IStorageService
    {
        StorageProviderType Provider { get; }

        /// <summary>Uploads an image, resizing server-side (the phone sends full-resolution stills).</summary>
        Task<StoredAsset?> UploadImageAsync(IFormFile file, string folderName, string fileName);

        /// <summary>
        /// Uploads a video byte-for-byte, with no transformation. The phone has already
        /// transcoded it to its delivery form; asking the provider to touch it again would burn
        /// per-second transcoding credits to produce a file we already have.
        /// </summary>
        Task<StoredAsset?> UploadVideoAsync(IFormFile file, string folderName, string fileName);

        /// <summary>
        /// Removes an asset. Returns false when the provider says it was not there — already gone
        /// is success as far as the caller is concerned, but the sweep logs the difference.
        /// </summary>
        Task<bool> DeleteAsync(string storageKey, EvidenceMediaType mediaType, CancellationToken cancellationToken = default);
    }
}
