namespace GameHubz.DataModels.Models
{
    public class PlayerMatchesDto
    {
        public PlayerStatsDto? Stats { get; set; }
        public List<PerformanceDto> Performance { get; set; } = new();
    }
}