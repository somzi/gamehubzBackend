namespace GameHubz.DataModels.Models
{
    public class MatchResultDto
    {
        public Guid MatchId { get; set; }
        public int HomeScore { get; set; }
        public int AwayScore { get; set; }
        public Guid TournamentId { get; set; }

        // Opt-in: when the result being edited would change the winner and the bracket has already
        // progressed downstream (next round / loser-bracket drop already played), reverting those
        // downstream results first is required. Old clients never send this, so the default keeps
        // the original "blocked with a lock message" behaviour byte-identical.
        public bool Cascade { get; set; }
    }
}
