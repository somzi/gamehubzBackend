namespace GameHubz.DataModels.Models
{
    public class TeamJoinRequestDto
    {
        public Guid RequestId { get; set; }
        public Guid UserId { get; set; }
        public string Username { get; set; } = "";
        public string? AvatarUrl { get; set; }
        public DateTime RequestedAt { get; set; }
    }
}
