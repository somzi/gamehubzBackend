namespace Template.DataModels.Models
{
    public class EmailModel
    {
        public string To { get; set; } = string.Empty;

        public string? Message { get; set; }

        public string? Cc { get; set; }

        public string? Subject { get; set; }

        public bool IsMessageHtml { get; set; }
    }
}