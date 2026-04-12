namespace GameHubz.DataModels.Models
{
    public class MatchListItemDto
    {
        public string TournamentName { get; set; } = string.Empty;
        public string HubName { get; set; } = string.Empty;
        public DateTime? ScheduledTime { get; set; }
        public string? Result { get; set; }
        public string OpponentName { get; set; } = string.Empty;
        public int? OpponentScore { get; set; }
        public int? UserScore { get; set; }
        public string? OpponentAvatarUrl { get; set; }
        public string? UserAvatarUrl { get; set; }
        public string? Username { get; set; }
    }
}