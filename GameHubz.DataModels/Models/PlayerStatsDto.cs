namespace GameHubz.DataModels.Models
{
    public class PlayerStatsDto
    {
        public int TotalMatches { get; set; }
        public int Wins { get; set; }
        public int Losses { get; set; }

        public int TournamentsWon { get; set; }

        // Distinct tournaments the player took part in, solo or through a team: the same rows the
        // profile's Tournaments tab lists.
        public int TournamentsPlayed { get; set; }

        // Most completed matches won in a row. A draw or a loss ends the run.
        public int LongestWinStreak { get; set; }

        public int Draws => TotalMatches - Wins - Losses;

        public double WinRate =>
            TotalMatches == 0 ? 0 : (double)Wins / TotalMatches * 100;
    }
}