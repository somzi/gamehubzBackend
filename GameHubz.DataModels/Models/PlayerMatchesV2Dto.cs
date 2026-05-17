namespace GameHubz.DataModels.Models
{
    public class PlayerMatchesV2Dto
    {
        public PlayerStatsDto? Stats { get; set; }
        public List<PerformanceV2Dto> Performance { get; set; } = new();
    }
}
