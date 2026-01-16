namespace GameHubz.DataModels.Models
{
    public class HubOverviewDto
    {
        public string Name { get; set; } = string.Empty;

        public string? Description { get; set; }

        public int NumberOfUsers { get; set; }

        public int NumberOfTournaments { get; set; }
    }
}