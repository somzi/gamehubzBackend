using System.Text.Json;
using GameHubz.DataModels.Enums;

namespace GameHubz.DataModels.Models
{
    public class NotificationDto
    {
        public Guid Id { get; set; }

        public string? Type { get; set; }

        public NotificationCategory Category { get; set; }

        public string Title { get; set; } = "";

        public string Body { get; set; } = "";

        /// <summary>The push payload as an object — the app hands it to the same router a push tap uses.</summary>
        public JsonElement? Data { get; set; }

        public DateTime CreatedOn { get; set; }

        public DateTime? ReadOn { get; set; }
    }
}
