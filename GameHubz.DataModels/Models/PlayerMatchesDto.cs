namespace GameHubz.DataModels.Models
{
    public class PlayerMatchesDto
    {
        public PlayerStatsDto? Stats { get; set; }
        public List<MatchListItemDto> LastMatches { get; set; } = new();
    }
}