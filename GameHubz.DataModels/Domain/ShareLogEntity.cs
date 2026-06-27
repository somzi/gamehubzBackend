using GameHubz.Common;
using GameHubz.DataModels.Enums;

namespace GameHubz.DataModels.Domain
{
    public class ShareLogEntity : BaseEntity
    {
        public Guid EntityId { get; set; }

        public ShareEntityType EntityType { get; set; }

        public string? Platform { get; set; }

        public string? IpAddress { get; set; }

        public string? UserAgent { get; set; }
    }
}
