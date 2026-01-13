namespace Template.Api.Models
{
    public class HandledErrorModel
    {
        public string Message { get; set; } = string.Empty;

        public string Details { get; set; } = string.Empty;

        public List<ValidationErrorItem>? Items { get; set; }
    }
}