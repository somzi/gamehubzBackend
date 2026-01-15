namespace GameHubz.DataModels.Models
{
    public class MatchResultDto
    {
        public Guid MatchId { get; set; }
        public int HomeScore { get; set; }
        public int AwayScore { get; set; }
        public Guid TournamentId { get; set; }
    }
}