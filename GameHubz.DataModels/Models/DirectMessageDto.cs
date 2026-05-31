namespace GameHubz.DataModels.Models
{
    public class DirectMessageDto
    {
        public Guid Id { get; set; }
        public Guid ChatId { get; set; }
        public Guid SenderId { get; set; }
        public string SenderUsername { get; set; } = "";
        public string? SenderAvatarUrl { get; set; }
        public string Content { get; set; } = "";
        public DateTime SentAt { get; set; }
        public bool IsRead { get; set; }
    }

    public class SendDirectMessageDto
    {
        public string Content { get; set; } = "";
    }
}
