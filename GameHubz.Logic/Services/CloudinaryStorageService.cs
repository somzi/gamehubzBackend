using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using GameHubz.Logic.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using System.Text.RegularExpressions;

namespace GameHubz.Logic.Services
{
    public class CloudinaryStorageService
    {
        private readonly Cloudinary cloudinary;

        public CloudinaryStorageService(IConfiguration config)
        {
            var account = new Account(
                config["Cloudinary:CloudName"],
                config["Cloudinary:ApiKey"],
                config["Cloudinary:ApiSecret"]
            );

            cloudinary = new Cloudinary(account);
            cloudinary.Api.Secure = true;
        }

        public async Task<string> UploadFileAsync(IFormFile file, string folderName, string fileName)
        {
            if (file == null || file.Length == 0) return null;

            const long maxFileSize = 20 * 1024 * 1024;

            if (file.Length > maxFileSize)
            {
                throw new BadHttpRequestException("File is too large. Maximum allowed size is 20MB.");
            }

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

            if (uploadResult.Error != null)
            {
                // A 4xx from Cloudinary means the file itself was rejected (not a real image,
                // unsupported/corrupt format) — that's a user error, so surface it as a 400
                // BusinessRuleException: friendly message, no ErrorLog noise. Anything else
                // (auth, rate-limit, 5xx, infra) is a genuine fault worth logging as a 500.
                int cloudinaryStatus = (int)uploadResult.StatusCode;
                if (cloudinaryStatus >= 400 && cloudinaryStatus < 500)
                {
                    throw new BusinessRuleException("The uploaded file could not be processed. Please make sure it is a valid image.");
                }

                throw new Exception($"Cloudinary upload failed: {uploadResult.Error.Message}");
            }

            if (uploadResult.SecureUrl == null)
            {
                throw new Exception("Cloudinary upload did not return a URL.");
            }

            return uploadResult.SecureUrl.ToString();
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