namespace GameHubz.DataModels.Models
{
    // Minimal projection used by the solo group/league stats resync — keeps the wire
    // payload tiny and avoids EF change-tracking on read-only data.
    public class GroupMatchStatsRow
    {
        public Guid HomeParticipantId { get; set; }

        // Null for Swiss bye matches — the home participant gets a free win.
        public Guid? AwayParticipantId { get; set; }
        // Headline score — the games-won tally under MatchWins, the raw score otherwise.
        public int HomeScore { get; set; }
        public int AwayScore { get; set; }

        // Real goals across every game of the series; what GF/GA is built from.
        public int HomeGoals { get; set; }
        public int AwayGoals { get; set; }

        public Guid? WinnerParticipantId { get; set; }
    }
}
