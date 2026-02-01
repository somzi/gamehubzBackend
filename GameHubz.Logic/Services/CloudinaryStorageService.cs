using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

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

            using var stream = file.OpenReadStream();

            var uploadParams = new ImageUploadParams
            {
                File = new FileDescription(fileName, stream),
                Folder = folderName,
                PublicId = fileName,
                Overwrite = true
            };

            var uploadResult = await cloudinary.UploadAsync(uploadParams);

            return uploadResult.SecureUrl.ToString();
        }
    }
}