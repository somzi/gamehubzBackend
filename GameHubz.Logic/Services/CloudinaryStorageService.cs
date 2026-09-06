using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using GameHubz.DataModels.Enums;
using GameHubz.Logic.Exceptions;
using GameHubz.Logic.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace GameHubz.Logic.Services
{
    public class CloudinaryStorageService : IStorageService
    {
        // Stills arrive at full phone resolution, so the resize happens here.
        private const long MaxImageSize = 20 * 1024 * 1024;

        // Video arrives already compressed on-device: a typical 20-40s screen recording lands at
        // 5-10MB, and the client's own ceiling (90s at 1080p) tops out near 22MB. The cap sits just
        // above that worst case rather than being generous — it is the backstop for a client that
        // skipped compression or a caller that bypassed the app, and every megabyte past it is
        // storage and bandwidth we pay for on a file that had no reason to be that large.
        // Moving the client's duration or bitrate means revisiting this number.
        private const long MaxVideoSize = 32 * 1024 * 1024;

        private readonly Cloudinary cloudinary;
        private readonly ILocalizationService localizationService;
        private readonly ILogger<CloudinaryStorageService> logger;

        public CloudinaryStorageService(
            IConfiguration config,
            ILocalizationService localizationService,
            ILogger<CloudinaryStorageService> logger)
        {
            this.localizationService = localizationService;
            this.logger = logger;

            var account = new Account(
                config["Cloudinary:CloudName"],
                config["Cloudinary:ApiKey"],
                config["Cloudinary:ApiSecret"]
            );

            cloudinary = new Cloudinary(account);
            cloudinary.Api.Secure = true;
        }

        public StorageProviderType Provider => StorageProviderType.Cloudinary;

        /// <summary>
        /// Back-compat entry point for the avatar call sites, which only ever want the URL.
        /// </summary>
        public async Task<string?> UploadFileAsync(IFormFile file, string folderName, string fileName)
            => (await UploadImageAsync(file, folderName, fileName))?.Url;

        public async Task<StoredAsset?> UploadImageAsync(IFormFile file, string folderName, string fileName)
        {
            if (file == null || file.Length == 0) return null;

            EnsureWithinSize(file, MaxImageSize);

            using var stream = file.OpenReadStream();

            var uploadParams = new ImageUploadParams
            {
                File = new FileDescription(fileName, stream),
                Folder = SanitizeFolder(folderName),
                PublicId = SanitizeSegment(fileName),
                Overwrite = true,
                Transformation = new Transformation().Width(800).Height(800).Crop("limit").Quality("auto").FetchFormat("auto")
            };

            var uploadResult = await cloudinary.UploadAsync(uploadParams);

            return ReadResult(uploadResult);
        }

        public async Task<StoredAsset?> UploadVideoAsync(IFormFile file, string folderName, string fileName)
        {
            if (file == null || file.Length == 0) return null;

            EnsureWithinSize(file, MaxVideoSize);

            using var stream = file.OpenReadStream();

            // No Transformation, deliberately. Cloudinary bills video transformation per second of
            // output, and the phone has already produced the exact file we intend to serve, so a
            // transcode here would pay for a second copy of what we just uploaded.
            var uploadParams = new VideoUploadParams
            {
                File = new FileDescription(fileName, stream),
                Folder = SanitizeFolder(folderName),
                PublicId = SanitizeSegment(fileName),
                Overwrite = true
            };

            var uploadResult = await cloudinary.UploadAsync(uploadParams);

            return ReadResult(uploadResult);
        }

        public async Task<bool> DeleteAsync(
            string storageKey,
            EvidenceMediaType mediaType,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(storageKey)) return false;

            // One asset per call, deliberately, even though the sweep deletes in batches and
            // Cloudinary offers a bulk delete. Bulk delete lives on the Admin API, which is rate
            // limited per hour; destroy is an Upload API call and is not. A sweep working off a
            // backlog would burn that hourly budget and start failing, so the chattier path is the
            // safer one here.
            var deletionParams = new DeletionParams(storageKey)
            {
                ResourceType = mediaType == EvidenceMediaType.Video ? ResourceType.Video : ResourceType.Image,
                // Evidence is being retired on purpose; leaving it warm on the CDN would keep it
                // reachable by anyone still holding the URL.
                Invalidate = true
            };

            var result = await cloudinary.DestroyAsync(deletionParams);

            // "not found" means somebody already removed it. The row can still be retired, so this
            // counts as done rather than as something the sweep should retry forever.
            if (string.Equals(result.Result, "not found", StringComparison.OrdinalIgnoreCase))
            {
                logger.LogInformation("Cloudinary asset {StorageKey} was already gone.", storageKey);
                return false;
            }

            if (!string.Equals(result.Result, "ok", StringComparison.OrdinalIgnoreCase))
            {
                throw new Exception($"Cloudinary delete failed for {storageKey}: {result.Result} {result.Error?.Message}");
            }

            return true;
        }

        private static void EnsureWithinSize(IFormFile file, long maxBytes)
        {
            if (file.Length > maxBytes)
            {
                throw new BadHttpRequestException(
                    $"File is too large. Maximum allowed size is {maxBytes / (1024 * 1024)}MB.");
            }
        }

        private StoredAsset ReadResult(RawUploadResult uploadResult)
        {
            if (uploadResult.Error != null)
            {
                // A 4xx from Cloudinary means the file itself was rejected (not real media,
                // unsupported/corrupt format) — that's a user error, so surface it as a 400
                // BusinessRuleException: friendly message, no ErrorLog noise. Anything else
                // (auth, rate-limit, 5xx, infra) is a genuine fault worth logging as a 500.
                int cloudinaryStatus = (int)uploadResult.StatusCode;
                if (cloudinaryStatus >= 400 && cloudinaryStatus < 500)
                {
                    throw new BusinessRuleException(this.localizationService["BusinessRule.UploadNotAnImage"]);
                }

                throw new Exception($"Cloudinary upload failed: {uploadResult.Error.Message}");
            }

            if (uploadResult.SecureUrl == null)
            {
                throw new Exception("Cloudinary upload did not return a URL.");
            }

            // PublicId is what Destroy takes. Recording it at upload time is the whole reason a
            // delete is possible later without parsing it back out of the URL.
            return new StoredAsset(
                uploadResult.SecureUrl.ToString(),
                uploadResult.PublicId,
                StorageProviderType.Cloudinary);
        }

        // Cloudinary rejects folders/public ids containing emoji or special characters
        // (e.g. a tournament named "CLASSIC TEAMS BLITZ🏆"), returning an error result
        // with a null SecureUrl. Strip anything outside the safe character set.
        private static string SanitizeFolder(string folderName)
        {
            if (string.IsNullOrWhiteSpace(folderName)) return folderName;

            var segments = folderName.Split('/', StringSplitOptions.RemoveEmptyEntries);
            return string.Join("/", segments.Select(SanitizeSegment));
        }

        private static string SanitizeSegment(string segment)
        {
            if (string.IsNullOrWhiteSpace(segment)) return segment;

            // Allow letters, digits, dash and underscore; collapse everything else to '_'.
            var cleaned = Regex.Replace(segment, @"[^a-zA-Z0-9_-]+", "_");
            return cleaned.Trim('_');
        }
    }
}
