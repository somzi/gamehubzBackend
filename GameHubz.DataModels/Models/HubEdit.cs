namespace GameHubz.DataModels.Models
{
    public class HubEdit
    {
        public Guid? Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public Guid UserId { get; set; }

        public List<UserHubEdit>? UserHubs { get; set; } = new();

        public List<TournamentEdit>? Tournaments { get; set; } = new();
    }
}