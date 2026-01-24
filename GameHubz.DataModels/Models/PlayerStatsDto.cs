namespace GameHubz.DataModels.Models
{
    public class PlayerStatsDto
    {
        public int TotalMatches { get; set; }
        public int Wins { get; set; }
        public int Losses { get; set; }

        public int TournamentsWon { get; set; }

        public int Draws => TotalMatches - Wins - Losses;

        public double WinRate =>
            TotalMatches == 0 ? 0 : (double)Wins / TotalMatches * 100;
    }
}