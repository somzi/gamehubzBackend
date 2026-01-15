using GameHubz.DataModels.Interfaces;

namespace GameHubz.DataModels.Models
{
    public class TournamentStagePost : IEditableDto
    {
        public Guid? Id { get; set; }

        public int Type { get; set; }

        public int Order { get; set; }

        public int? QualifiedPlayersCount { get; set; }

        public Guid? TournamentId { get; set; }

        public Guid? TournamentGroupId { get; set; }
    }
}