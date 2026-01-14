namespace GameHubz.DataModels.Models
{
    public class PlayerStatsDto
    {
        public int TotalMatches { get; set; }
        public int Wins { get; set; }
        public int Losses { get; set; }

        public double WinRate =>
            TotalMatches == 0 ? 0 : (double)Wins / TotalMatches * 100;
    }
}