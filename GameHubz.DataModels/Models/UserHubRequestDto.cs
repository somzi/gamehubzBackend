namespace GameHubz.DataModels.Models
{
    public class UserHubRequestDto
    {
        public Guid RequestId { get; set; }
        public Guid UserId { get; set; }
        public Guid HubId { get; set; }
        public string Username { get; set; } = "";
        public string? AvatarUrl { get; set; }
        public DateTime RequestedAt { get; set; }
    }
}
