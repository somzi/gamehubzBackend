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

        // Sent from the bracket, the organizer's screen: an organizer who plays the match enters it
        // there as the organizer, outside result verification. Without it (the match sheet on Home)
        // they report as the player they are. Ignored for anyone who does not manage the tournament.
        public bool AsOrganizer { get; set; }
    }
}
