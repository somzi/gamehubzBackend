using Microsoft.AspNetCore.Http;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using GameHubz.DataModels.Config;

namespace GameHubz.Logic.Services
{
    public class ImageService
    {
        private readonly BlobService blobService;

        public ImageService(BlobService blobService)
        {
            this.blobService = blobService;
        }

        public async Task<BlobInfo?> ResizeAndUploadThumbnail(IFormFile formFile, string blobName)
        {
            using Stream fileStream = formFile.OpenReadStream();

            var resizedFileStream = ResizeImage(fileStream);
            resizedFileStream.Position = 0;

            var blobInfo = await this.blobService.Upload(
                resizedFileStream,
                containerName: BlobContainers.Thumbnails,
                blobName: BlobUrlHelper.GetThumbnailName(blobName));

            return blobInfo;
        }

        private static MemoryStream ResizeImage(Stream file, int maxSize = 100)
        {
            var outStream = new MemoryStream();

            using var image = Image.Load(file);

            double ratio = image.Width > image.Height
                ? (double)maxSize / image.Width
                : (double)maxSize / image.Height;

            int width = (int)(image.Width * ratio);
            int height = (int)(image.Height * ratio);

            image.Mutate(
                 i => i.Resize(width, height)
                        .Crop(new Rectangle(0, 0, width, height)));

            image.Save(outStream, new SixLabors.ImageSharp.Formats.Jpeg.JpegEncoder { Quality = 90 });

            return outStream;
        }
    }
}
