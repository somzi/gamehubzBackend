namespace GameHubz.DataModels.Models
{
    /// <summary>Head-to-head summary between two users — everything from the "me" perspective
    /// (MyWins = the caller's wins over the opponent). LastMatch fields are null when there are
    /// no completed matches between the two.</summary>
    public class HeadToHeadDto
    {
        public int TotalMatches { get; set; }
        public int MyWins { get; set; }
        public int OpponentWins { get; set; }
        public int Draws { get; set; }

        public DateTime? LastMatchTime { get; set; }
        public int? LastMyScore { get; set; }
        public int? LastOpponentScore { get; set; }

        /// <summary>"W" / "L" / "D" from the caller's perspective, null when no matches.</summary>
        public string? LastOutcome { get; set; }

        public string? LastTournamentName { get; set; }
        public string? LastHubName { get; set; }
    }
}
