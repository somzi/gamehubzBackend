namespace GameHubz.DataModels.Models
{
    public class LeagueStandingDto
    {
        public int Position { get; set; }
        public Guid ParticipantId { get; set; }
        public Guid UserId { get; set; }
        public int Points { get; set; }
        public int MatchesPlayed { get; set; }
        public int Wins { get; set; }
        public int Draws { get; set; }
        public int Losses { get; set; }
        public int GoalsFor { get; set; }
        public int GoalsAgainst { get; set; }
        public int GoalDifference { get; set; }
    }
}