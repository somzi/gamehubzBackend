namespace GameHubz.DataModels.Models
{
    /// <summary>Small slice of the next scheduled tournament in a hub — feeds the /hubinfo card.</summary>
    public class HubNextTournamentDto
    {
        public string Name { get; set; } = string.Empty;
        public DateTime? StartDate { get; set; }
    }

    /// <summary>Small slice of the most recent champion in a hub — feeds the /hubinfo card.</summary>
    public class HubLatestChampionDto
    {
        public string ChampionName { get; set; } = string.Empty;
        public string TournamentName { get; set; } = string.Empty;
    }
}
