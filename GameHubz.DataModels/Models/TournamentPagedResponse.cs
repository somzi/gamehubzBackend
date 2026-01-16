namespace GameHubz.DataModels.Models
{
    public class TournamentPagedResponse
    {
        public List<TournamentDto> Tournaments { get; set; } = [];

        public int Count { get; set; }
    }
}