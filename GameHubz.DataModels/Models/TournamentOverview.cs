using GameHubz.DataModels.Enums;

namespace GameHubz.DataModels.Models
{
    public class TournamentOverview
    {
        public string Name { get; set; } = string.Empty;
        public RegionType Region { get; set; }
        public DateTime StartDate { get; set; }
        public int NumberOfParticipants { get; set; }
        public int Prize { get; set; }
        public PrizeCurrency PrizeCurrency { get; set; }
    }
}