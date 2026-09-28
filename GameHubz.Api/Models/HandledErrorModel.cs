namespace GameHubz.Api.Models
{
    public class HandledErrorModel
    {
        public string Message { get; set; } = string.Empty;

        // Stable identifier for client recovery; the displayed message remains localized.
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public string? ErrorCode { get; set; }

        public string Details { get; set; } = string.Empty;

        /// <summary>
        /// Reference id of the persisted <c>ErrorLog</c> row for server faults (5xx).
        /// Null for handled 4xx responses. The client can show it so a user can report
        /// the exact failure.
        /// </summary>
        public string? ErrorId { get; set; }

        public List<ValidationErrorItem>? Items { get; set; }
    }
}
