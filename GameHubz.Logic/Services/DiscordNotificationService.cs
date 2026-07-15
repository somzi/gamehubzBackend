using GameHubz.Logic.Interfaces;
using Microsoft.Extensions.Logging;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace GameHubz.Logic.Services
{
    /// <summary>
    /// POSTs a rendered announcement card to a Discord webhook URL as a multipart upload
    /// (payload_json + files[0]) — the same wire format the slash-command followups use.
    /// Swallow-everything error handling: a Discord outage or a revoked webhook must never
    /// surface into the request that triggered the notification.
    /// </summary>
    public class DiscordNotificationService : IDiscordNotificationService
    {
        private readonly IHttpClientFactory httpClientFactory;
        private readonly ILogger<DiscordNotificationService> logger;

        public DiscordNotificationService(
            IHttpClientFactory httpClientFactory,
            ILogger<DiscordNotificationService> logger)
        {
            this.httpClientFactory = httpClientFactory;
            this.logger = logger;
        }

        public async Task SendImageAsync(string webhookUrl, byte[] png, string filename)
        {
            if (string.IsNullOrWhiteSpace(webhookUrl))
                return;

            try
            {
                var client = httpClientFactory.CreateClient("DiscordWebhook");

                using var form = new MultipartFormDataContent();
                string payload = JsonSerializer.Serialize(new
                {
                    attachments = new[] { new { id = 0, filename } }
                });
                form.Add(new StringContent(payload, Encoding.UTF8, "application/json"), "payload_json");

                var imageContent = new ByteArrayContent(png);
                imageContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
                form.Add(imageContent, "files[0]", filename);

                using var response = await client.PostAsync(webhookUrl, form);

                if (!response.IsSuccessStatusCode)
                {
                    var errorBody = await response.Content.ReadAsStringAsync();
                    logger.LogWarning("Discord webhook returned {StatusCode}: {Body}", response.StatusCode, errorBody);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to send Discord webhook notification");
            }
        }
    }
}
