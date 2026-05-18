namespace GameHubz.DataModels.Models
{
    public class TranslateMessageResponseDto
    {
        public string OriginalText { get; set; } = string.Empty;

        public string TranslatedText { get; set; } = string.Empty;

        public string DetectedSourceLanguage { get; set; } = string.Empty;
    }
}
