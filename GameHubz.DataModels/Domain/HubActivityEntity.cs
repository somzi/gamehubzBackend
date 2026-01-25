using GameHubz.Common;
using GameHubz.DataModels.Enums;

namespace GameHubz.DataModels.Domain
{
    public class HubActivityEntity : BaseEntity
    {
        public HubActivityType Type { get; set; }

        public Guid? HubId { get; set; }

        public HubEntity? Hub { get; set; }

        public Guid? TournamentId { get; set; }

        public TournamentEntity? Tournament { get; set; }
    }
}