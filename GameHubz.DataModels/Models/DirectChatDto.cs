namespace GameHubz.DataModels.Models
{
    public class DirectChatDto
    {
        public Guid Id { get; set; }
        public Guid OtherUserId { get; set; }
        public string OtherUsername { get; set; } = "";
        public string? OtherNickname { get; set; }
        public string? OtherAvatarUrl { get; set; }
        public string? LastMessage { get; set; }
        public DateTime? LastMessageAt { get; set; }
        public Guid? LastMessageSenderId { get; set; }
        public int UnreadCount { get; set; }
    }
}
