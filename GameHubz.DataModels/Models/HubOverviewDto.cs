namespace GameHubz.DataModels.Models
{
    public class HubOverviewDto
    {
        public string Name { get; set; } = string.Empty;

        public Guid Id { get; set; }

        public string? Description { get; set; }

        public int NumberOfUsers { get; set; }

        public int NumberOfTournaments { get; set; }

        public bool IsUserFollowHub { get; set; }
        public bool IsUserOwner { get; set; }
        public List<HubSocialDto> HubSocials { get; set; } = [];
        public Guid UserId { get; set; }
        public string? AvatarUrl { get; set; }
    }
}