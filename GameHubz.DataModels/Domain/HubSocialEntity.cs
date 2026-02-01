using GameHubz.Common;
using GameHubz.DataModels.Enums;

namespace GameHubz.DataModels.Domain
{
    public class HubSocialEntity : BaseEntity
    {
        public string Username { get; set; } = "";

        public SocialType Type { get; set; }

        public Guid? HubId { get; set; }

        public HubEntity? Hub { get; set; }
    }
}