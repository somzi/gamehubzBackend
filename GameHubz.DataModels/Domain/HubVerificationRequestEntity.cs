using GameHubz.Common;
using GameHubz.DataModels.Enums;

namespace GameHubz.DataModels.Domain
{
    public class HubVerificationRequestEntity : BaseEntity
    {
        public Guid? HubId { get; set; }

        public HubEntity? Hub { get; set; }

        public string Reason { get; set; } = string.Empty;

        public HubVerificationStatus Status { get; set; } = HubVerificationStatus.Pending;
    }
}
