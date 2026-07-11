using GameHubz.Logic.Interfaces;
using Microsoft.Extensions.Logging;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GameHubz.Logic.Services
{
    /// <summary>
    /// POSTs an embed payload to a Discord webhook URL. Same shape as <see cref="NotificationService"/>
    /// (IHttpClientFactory + swallow-everything error handling): a Discord outage or a revoked webhook
    /// must never surface into the request that triggered the notification.
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

        public async Task SendAsync(string webhookUrl, object payload)
        {
            if (string.IsNullOrWhiteSpace(webhookUrl))
                return;

            try
            {
                var client = httpClientFactory.CreateClient("DiscordWebhook");

                var response = await client.PostAsJsonAsync(webhookUrl, payload, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
                });

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
