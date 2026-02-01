namespace GameHubz.DataModels.Models
{
    public class ChatMessageDto
    {
        public Guid Id { get; set; }
        public Guid UserId { get; set; }
        public string UserNickname { get; set; } = string.Empty;
        public string? UserAvatarUrl { get; set; } // Ako imaš avatare
        public string Content { get; set; } = string.Empty;
        public DateTime SentAt { get; set; }
    }
}