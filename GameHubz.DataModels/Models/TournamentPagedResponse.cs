namespace GameHubz.DataModels.Models
{
    public class TournamentPagedResponse
    {
        public List<TournamentOverview> Tournaments { get; set; } = [];

        public int Count { get; set; }
    }
}