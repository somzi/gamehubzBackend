namespace GameHubz.Api.Models
{
    public class HandledErrorModel
    {
        public string Message { get; set; } = string.Empty;

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
