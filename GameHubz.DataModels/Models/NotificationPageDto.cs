namespace GameHubz.DataModels.Models
{
    public class NotificationPageDto
    {
        public List<NotificationDto> Items { get; set; } = new();

        /// <summary>Opaque cursor for the next (older) page; null when this page reaches the end.</summary>
        public string? NextCursor { get; set; }

        /// <summary>How many days the server keeps a notification — quoted by the app's end-of-list footer.</summary>
        public int RetentionDays { get; set; }
    }
}
