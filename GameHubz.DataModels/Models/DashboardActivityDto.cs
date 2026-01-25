using GameHubz.DataModels.Enums;

namespace GameHubz.DataModels.Models
{
    public class DashboardActivityDto
    {
        public string HubName { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string TournamentName { get; set; } = string.Empty;
        public string TimeAgo { get; set; } = string.Empty;
        public DateTime CreatedOn { get; set; }
        public HubActivityType Type { get; set; }
    }
}