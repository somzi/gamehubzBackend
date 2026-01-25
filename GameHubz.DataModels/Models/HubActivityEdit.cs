using GameHubz.DataModels.Enums;

namespace GameHubz.DataModels.Models
{
    public class HubActivityEdit
    {
        public Guid? Id { get; set; }

        public HubActivityType Type { get; set; }

        public Guid? HubId { get; set; }

        public Guid? TournamentId { get; set; }
    }
}