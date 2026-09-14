namespace GameHubz.DataModels.Models
{
    public class NotificationPageDto
    {
        public List<NotificationDto> Items { get; set; } = new();

        /// <summary>Opaque cursor for the next (older) page; null when this page reaches the end.</summary>
        public string? NextCursor { get; set; }
    }
}
