namespace GameHubz.Logic.Services
{
    /// <summary>
    /// Shared Discord plumbing for the domain notifiers (<see cref="TournamentNotifier"/>,
    /// <see cref="MatchNotifier"/>, <see cref="BracketNotifier"/>): resolve the hub's webhook +
    /// per-event settings, and fire-and-forget the announcement card. Deliberately NOT a generic
    /// notification provider abstraction — there are exactly two channels (Expo, Discord) and the
    /// Expo side lives directly in the concrete notifiers.
    /// </summary>
    public abstract class DiscordNotifierBase
    {
        protected readonly IAppUnitOfWork AppUnitOfWork;
        private readonly IDiscordNotificationService discordNotificationService;

        protected DiscordNotifierBase(
            IUnitOfWorkFactory unitOfWorkFactory,
            IDiscordNotificationService discordNotificationService)
        {
            this.AppUnitOfWork = unitOfWorkFactory.CreateAppUnitOfWork();
            this.discordNotificationService = discordNotificationService;
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
        /// Fire-and-forget render + POST, mirroring the Task.Run pattern the Expo pushes use: the
        /// QuestPDF raster (~100ms) and the send run outside the request and must never block or
        /// fail it. All data is resolved into the card DTO before this call, so the background
        /// task never touches the request-scoped DbContext (F109 discipline).
        /// </summary>
        protected void SendToDiscord(string webhookUrl, AnnouncementCardData card)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    card.GeneratedAtUtc = DateTime.UtcNow;
                    byte[] png = DiscordAnnouncementCard.Render(card);
                    await this.discordNotificationService.SendImageAsync(webhookUrl, png, "announcement.png");
                }
                catch { /* fire-and-forget */ }
            });
        }
    }
}
