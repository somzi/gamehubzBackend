namespace GameHubz.Logic.Services
{
    /// <summary>
    /// Shared Discord plumbing for the domain notifiers (<see cref="TournamentNotifier"/>,
    /// <see cref="MatchNotifier"/>, <see cref="BracketNotifier"/>): resolve the hub's webhook +
    /// per-event settings, and fire-and-forget the POST. Deliberately NOT a generic notification
    /// provider abstraction — there are exactly two channels (Expo, Discord) and the Expo side
    /// lives directly in the concrete notifiers.
    /// </summary>
    public abstract class DiscordNotifierBase
    {
        protected readonly IAppUnitOfWork AppUnitOfWork;
        protected readonly DiscordEmbedBuilder EmbedBuilder;
        private readonly IDiscordNotificationService discordNotificationService;

        protected DiscordNotifierBase(
            IUnitOfWorkFactory unitOfWorkFactory,
            IDiscordNotificationService discordNotificationService,
            DiscordEmbedBuilder embedBuilder)
        {
            this.AppUnitOfWork = unitOfWorkFactory.CreateAppUnitOfWork();
            this.discordNotificationService = discordNotificationService;
            this.EmbedBuilder = embedBuilder;
        }

        /// <summary>
        /// Loads the hub and answers whether this event should go out to Discord. Null when the hub
        /// has no webhook configured or the event is switched off in its settings JSON.
        /// </summary>
        protected async Task<HubEntity?> GetDiscordTargetAsync(Guid? hubId, Func<DiscordNotificationSettings, bool> eventEnabled)
        {
            if (!hubId.HasValue) return null;

            var hub = await this.AppUnitOfWork.HubRepository.GetById(hubId.Value);
            if (hub == null || string.IsNullOrWhiteSpace(hub.DiscordWebhookUrl)) return null;

            return eventEnabled(DiscordNotificationSettings.Parse(hub.DiscordNotificationSettings)) ? hub : null;
        }

        /// <summary>
        /// Fire-and-forget POST, mirroring the Task.Run pattern the Expo pushes use: the send runs
        /// outside the request and must never block or fail it. All data is resolved before this
        /// call, so the background task never touches the request-scoped DbContext (F109 discipline).
        /// </summary>
        protected void SendToDiscord(string webhookUrl, object payload)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await this.discordNotificationService.SendAsync(webhookUrl, payload);
                }
                catch { /* fire-and-forget */ }
            });
        }
    }
}
