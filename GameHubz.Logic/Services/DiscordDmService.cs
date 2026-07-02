using GameHubz.Logic.Interfaces;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace GameHubz.Logic.Services
{
    /// <summary>
    /// HTTP-only bot DM sender (no gateway connection): POST users/@me/channels to open the DM
    /// channel, then POST channels/{id}/messages. Uses the "DiscordBot" named client (BaseAddress +
    /// Bot authorization preset in Program.cs). See <see cref="IDiscordDmService"/> for the contract.
    /// </summary>
    public class DiscordDmService : IDiscordDmService
    {
        // DM channel ids are stable per recipient, so cache them in-process to skip the extra
        // create-channel round-trip on repeat sends. Deliberately a static dictionary (the service
        // is transient) and deliberately NOT the request-scoped ICacheService — the fire-and-forget
        // path outlives the request scope. Entries are tiny; unbounded growth is not a realistic
        // concern at this user count.
        private static readonly ConcurrentDictionary<string, string> DmChannelIdsByUser = new();

        private readonly IHttpClientFactory httpClientFactory;
        private readonly ILogger<DiscordDmService> logger;

        public DiscordDmService(
            IHttpClientFactory httpClientFactory,
            ILogger<DiscordDmService> logger)
        {
            this.httpClientFactory = httpClientFactory;
            this.logger = logger;
        }

        public async Task SendDmAsync(string discordUserId, string content)
        {
            if (string.IsNullOrWhiteSpace(discordUserId) || string.IsNullOrWhiteSpace(content))
                return;

            try
            {
                var client = httpClientFactory.CreateClient("DiscordBot");

                // A client without the bot token configured means the integration is off.
                if (client.DefaultRequestHeaders.Authorization == null)
                    return;

                var channelId = await GetOrCreateDmChannelIdAsync(client, discordUserId);
                if (channelId == null)
                    return;

                var response = await client.PostAsJsonAsync(
                    $"channels/{channelId}/messages",
                    new { content });

                if (response.StatusCode == HttpStatusCode.Forbidden)
                {
                    // Expected: the user blocked bot DMs or shares no server with the bot. The
                    // cached channel id stays valid, so just skip quietly.
                    logger.LogWarning("Discord DM to {DiscordUserId} forbidden (user blocks DMs or shares no server with the bot).", discordUserId);
                    return;
                }

                if (!response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync();
                    logger.LogWarning("Discord DM send returned {StatusCode}: {Body}", response.StatusCode, body);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to send Discord DM to {DiscordUserId}.", discordUserId);
            }
        }

        public void SendDmInBackground(string? discordUserId, string content)
        {
            if (string.IsNullOrWhiteSpace(discordUserId))
                return;

            var id = discordUserId;
            _ = Task.Run(async () =>
            {
                try
                {
                    await SendDmAsync(id, content);
                }
                catch { /* fire-and-forget */ }
            });
        }

        private async Task<string?> GetOrCreateDmChannelIdAsync(HttpClient client, string discordUserId)
        {
            if (DmChannelIdsByUser.TryGetValue(discordUserId, out var cached))
                return cached;

            var response = await client.PostAsJsonAsync(
                "users/@me/channels",
                new { recipient_id = discordUserId });

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                logger.LogWarning("Discord create-DM-channel for {DiscordUserId} returned {StatusCode}: {Body}", discordUserId, response.StatusCode, body);
                return null;
            }

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var channelId = doc.RootElement.GetProperty("id").GetString();
            if (channelId != null)
                DmChannelIdsByUser[discordUserId] = channelId;

            return channelId;
        }
    }
}
