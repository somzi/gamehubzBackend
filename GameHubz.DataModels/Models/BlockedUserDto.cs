namespace GameHubz.DataModels.Models
{
    public class BlockedUserDto
    {
        public Guid UserId { get; set; }
        public string Username { get; set; } = "";
        public string? AvatarUrl { get; set; }
        public DateTime BlockedAt { get; set; }
    }
}
