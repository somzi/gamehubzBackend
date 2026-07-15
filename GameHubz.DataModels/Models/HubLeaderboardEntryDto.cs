namespace GameHubz.DataModels.Models
{
    /// <summary>One player row on the /leaderboard Discord card. Aggregated across all
    /// completed matches in a single hub's tournaments — the same denominator across every
    /// sort mode, so trophy / wins / win-rate rankings stay comparable.</summary>
    public class HubLeaderboardEntryDto
    {
        public Guid UserId { get; set; }
        public string Username { get; set; } = string.Empty;
        public string? Nickname { get; set; }
        public string? AvatarUrl { get; set; }

        public int Trophies { get; set; }
        public int Wins { get; set; }
        public int Losses { get; set; }
        public int Draws { get; set; }
        public int TotalMatches { get; set; }
    }
}
