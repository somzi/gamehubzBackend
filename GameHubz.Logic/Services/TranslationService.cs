using DeepL;
using DeepL.Model;
using Microsoft.Extensions.Configuration;

namespace GameHubz.Logic.Services
{
    public class TranslationService : IDisposable
    {
        private readonly Translator translator;

        public TranslationService(IConfiguration configuration)
        {
            var apiKey = configuration["DeepL:ApiKey"];

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                throw new InvalidOperationException(
                    "DeepL API key is not configured. Set 'DeepL:ApiKey' in appsettings.json.");
            }

            this.translator = new Translator(apiKey);
        }

        public async Task<TranslateMessageResponseDto> TranslateAsync(TranslateMessageDto request)
        {
            if (request is null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            if (string.IsNullOrWhiteSpace(request.Text))
            {
                throw new ArgumentException("Text to translate must not be empty.", nameof(request));
            }

            if (string.IsNullOrWhiteSpace(request.TargetLanguage))
            {
                throw new ArgumentException("Target language must not be empty.", nameof(request));
            }

            TextResult result = await this.translator.TranslateTextAsync(
                request.Text,
                sourceLanguageCode: null,
                targetLanguageCode: request.TargetLanguage);

            return new TranslateMessageResponseDto
            {
                OriginalText = request.Text,
                TranslatedText = result.Text,
                DetectedSourceLanguage = result.DetectedSourceLanguageCode,
            };
        }

        public void Dispose()
        {
            this.translator.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}