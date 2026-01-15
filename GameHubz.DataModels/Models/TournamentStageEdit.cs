namespace GameHubz.DataModels.Models
{
    public class TournamentStageEdit
    {
        public Guid? Id { get; set; }

        public int Type { get; set; }

        public int Order { get; set; }

        public int? QualifiedPlayersCount { get; set; }

        public Guid? TournamentId { get; set; }

        public Guid? TournamentGroupId { get; set; }

        public List<MatchEdit>? Matchs { get; set; } = new();

        public List<TournamentGroupEdit>? TournamentGroups { get; set; } = new();
    }
}