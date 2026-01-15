namespace GameHubz.DataModels.Models
{
    public class TournamentStageDto
    {
        public Guid? Id { get; set; }
        public int Type { get; set; }

        public int Order { get; set; }

        public int? QualifiedPlayersCount { get; set; }

        public Guid? TournamentId { get; set; }

        public Guid? TournamentGroupId { get; set; }

        public List<MatchDto>? Matchs { get; set; } = new();

        public List<TournamentGroupDto>? TournamentGroups { get; set; } = new();


    }
}