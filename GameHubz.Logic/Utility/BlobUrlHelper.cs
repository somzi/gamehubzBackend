using GameHubz.DataModels.Config;
using Microsoft.Extensions.Configuration;

namespace GameHubz.Logic.Utility
{
    public static class BlobUrlHelper
    {
        private const string thumbnailPrefex = "thumb-";

        public static string GetContainerName(AssetType assetType)
        {
            return assetType switch
            {
                AssetType.Document => BlobContainers.Documents,
                AssetType.Image => BlobContainers.Images,
                AssetType.EmailAttachment => BlobContainers.EmailAttachment,
                _ => throw new NotImplementedException(),
            };
        }

        public static bool HasThumbnail(AssetType assetType)
        {
            return assetType == AssetType.Image;
        }

        public static string GetThumbnailName(string blobName)
        {
            return $"{thumbnailPrefex}{blobName}";
        }

        public static string GetUrl(IConfiguration configuration, AssetEntity assetEntity)
        {
            return $"{GetBaseUrl(configuration)}/{GetContainerName(assetEntity.AssetType)}/{assetEntity.BlobName}";
        }

        public static string GetThumbUrl(IConfiguration configuration, AssetEntity assetEntity)
        {
            if (!HasThumbnail(assetEntity.AssetType))
            {
                return "";
            }

            return $"{GetBaseUrl(configuration)}/{BlobContainers.Thumbnails}/{GetThumbnailName(assetEntity.BlobName)}";
        }

        private static string GetBaseUrl(IConfiguration configuration)
        {
            return configuration["BlobConfig:BlobBaseUrl"]!;
        }
    }
}
