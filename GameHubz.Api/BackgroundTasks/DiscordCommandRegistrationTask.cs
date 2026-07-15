using GameHubz.DataModels.Config;
using Microsoft.Extensions.Options;
using System.Net.Http.Json;

namespace GameHubz.Api.BackgroundTasks
{
    /// <summary>
    /// One-shot startup task that idempotently (re-)registers the bot's global slash commands via
    /// PUT applications/{appId}/commands (a bulk overwrite, so re-running is always safe). Opt-in
    /// through Discord:RegisterCommandsOnStartup so ordinary deploys don't touch Discord at all.
    /// Runs detached from startup and swallows every failure — command registration must never
    /// keep the API from booting.
    /// </summary>
    public class DiscordCommandRegistrationTask : IHostedService
    {
        private readonly IHttpClientFactory httpClientFactory;
        private readonly DiscordConfig config;
        private readonly ILogger<DiscordCommandRegistrationTask> logger;

        public DiscordCommandRegistrationTask(
            IHttpClientFactory httpClientFactory,
            IOptions<DiscordConfig> discordOptions,
            ILogger<DiscordCommandRegistrationTask> logger)
        {
            this.httpClientFactory = httpClientFactory;
            this.config = discordOptions.Value;
            this.logger = logger;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            if (!config.RegisterCommandsOnStartup)
                return Task.CompletedTask;

            if (string.IsNullOrWhiteSpace(config.ApplicationId) || string.IsNullOrWhiteSpace(config.BotToken))
            {
                logger.LogWarning("Discord:RegisterCommandsOnStartup is on but ApplicationId/BotToken are missing — skipping command registration.");
                return Task.CompletedTask;
            }

            _ = Task.Run(() => RegisterCommandsAsync(), CancellationToken.None);
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        private async Task RegisterCommandsAsync()
        {
            try
            {
                // Discord requires a homogeneous array shape when we mix commands with/without
                // options — hand-build the anonymous objects as `object` so `vs` can carry its
                // opponent option (application-command option type 6 = USER).
                var commands = new object[]
                {
                    new { name = "nextmatch", description = "Show your next GameHubz match", type = 1 },
                    new { name = "matches", description = "List your active GameHubz matches", type = 1 },
                    new { name = "lastmatches", description = "Show your recent GameHubz results", type = 1 },
                    new { name = "profile", description = "Show your GameHubz profile and stats", type = 1 },
                    new
                    {
                        name = "vs",
                        description = "Head-to-head vs another linked player",
                        type = 1,
                        options = new[]
                        {
                            new { name = "opponent", description = "The player to compare against", type = 6, required = true },
                        },
                    },
                };

                var client = httpClientFactory.CreateClient("DiscordBot");
                var response = await client.PutAsJsonAsync($"applications/{config.ApplicationId}/commands", commands);

                if (response.IsSuccessStatusCode)
                {
                    logger.LogInformation("Discord slash commands registered ({Count}).", commands.Length);
                }
                else
                {
                    logger.LogWarning("Discord command registration returned {StatusCode}: {Body}",
                        response.StatusCode, await response.Content.ReadAsStringAsync());
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Discord command registration failed.");
            }
        }
    }
}
