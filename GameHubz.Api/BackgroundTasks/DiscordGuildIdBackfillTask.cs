using GameHubz.DataModels.Config;
using GameHubz.Logic.Interfaces;
using GameHubz.Logic.Services;
using Microsoft.Extensions.Options;

namespace GameHubz.Api.BackgroundTasks
{
    /// <summary>
    /// One-shot startup task that fills in Hub.DiscordGuildId for hubs whose webhook URL predates
    /// the guild-id column (migration 68). Iterates hubs sequentially so a burst of GETs can't
    /// trip Discord's per-IP rate limit, and skips silently if the opt-in flag is off. Detached
    /// from startup so a slow Discord response can't keep the API from booting.
    /// </summary>
    public class DiscordGuildIdBackfillTask : IHostedService
    {
        private readonly IServiceScopeFactory scopeFactory;
        private readonly DiscordConfig config;
        private readonly ILogger<DiscordGuildIdBackfillTask> logger;

        public DiscordGuildIdBackfillTask(
            IServiceScopeFactory scopeFactory,
            IOptions<DiscordConfig> discordOptions,
            ILogger<DiscordGuildIdBackfillTask> logger)
        {
            this.scopeFactory = scopeFactory;
            this.config = discordOptions.Value;
            this.logger = logger;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            if (!config.BackfillGuildIdsOnStartup)
                return Task.CompletedTask;

            _ = Task.Run(() => BackfillAsync(), CancellationToken.None);
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        private async Task BackfillAsync()
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var uowFactory = scope.ServiceProvider.GetRequiredService<IUnitOfWorkFactory>();
                var uow = uowFactory.CreateAppUnitOfWork();
                var hubService = scope.ServiceProvider.GetRequiredService<HubService>();

                var hubs = await uow.HubRepository.GetWithWebhookMissingGuildId();
                logger.LogInformation("Discord guild-id backfill: {Count} hub(s) to process.", hubs.Count);

                // Repository reads are AsNoTracking — the entities come back detached, so a bare
                // SaveChangesAsync would silently persist nothing. UpdateEntity re-attaches each
                // hub as Modified; the anonymous reader stamps ModifiedBy with the system user
                // (there's no HTTP context in a hosted task).
                var systemContext = new AnonymousUserContextReader();

                int filled = 0;
                foreach (var hub in hubs)
                {
                    if (string.IsNullOrWhiteSpace(hub.DiscordWebhookUrl))
                        continue;

                    string? guildId = await hubService.ResolveDiscordGuildIdAsync(hub.Id!.Value, hub.DiscordWebhookUrl);
                    if (guildId == null)
                        continue;

                    hub.DiscordGuildId = guildId;
                    await uow.HubRepository.UpdateEntity(hub, systemContext);
                    filled++;

                    // Save one at a time so a mid-batch failure doesn't roll back earlier hubs.
                    await uow.SaveChangesAsync();
                }

                logger.LogInformation("Discord guild-id backfill complete: {Filled}/{Total} hub(s) linked.", filled, hubs.Count);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Discord guild-id backfill failed.");
            }
        }
    }
}
