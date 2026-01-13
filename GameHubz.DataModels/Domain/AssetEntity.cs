using GameHubz.Common;
using GameHubz.Common.Enums;

namespace GameHubz.DataModels.Domain
{
    public class AssetEntity : BaseEntity
    {
        public string FileName { get; set; } = "";

        public string FileFormat { get; set; } = "";

        public string BlobName { get; set; } = "";

        public string Extension { get; set; } = "";

        public string Description { get; set; } = "";

        public AssetType AssetType { get; set; }

        public long Size { get; set; }

        public UserEntity? CreatedByUser { get; set; }

        public UserEntity? ModifiedByUser { get; set; }
    }
}
