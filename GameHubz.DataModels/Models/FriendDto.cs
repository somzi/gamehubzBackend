namespace GameHubz.DataModels.Models
{
    public class FriendDto
    {
        public Guid UserId { get; set; }
        public string Username { get; set; } = "";
        public string? Nickname { get; set; }
        public string? AvatarUrl { get; set; }
        public DateTime FriendsSince { get; set; }
    }
}
