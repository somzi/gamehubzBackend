using GameHubz.Common;

namespace GameHubz.DataModels.Domain
{
    public class HubEntity : BaseEntity
    {
        public string Name { get; set; } = string.Empty;
        public string? Description { get; set; }
        public Guid UserId { get; set; }
        public UserEntity User { get; set; } = null!;
        public List<UserHubEntity>? UserHubs { get; set; } = new();
        public List<TournamentEntity>? Tournaments { get; set; } = new();
    }
}