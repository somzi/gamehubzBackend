namespace GameHubz.DataModels.Models
{
    public class MatchListItemDto
    {
        public string TournamentName { get; set; } = string.Empty;
        public DateTime? ScheduledTime { get; set; }
        public bool IsWin { get; set; }
        public string OpponentName { get; set; } = string.Empty;
        public int? OpponentScore { get; set; }
        public int? UserScore { get; set; }
    }
}