namespace GameHubz.Logic.Services
{
    /// <summary>
    /// Facade for bracket-lifecycle notifications (see <see cref="TournamentNotifier"/> for the
    /// pattern). Currently one event: the tournament going live once its bracket is generated.
    /// </summary>
    public class BracketNotifier : DiscordNotifierBase
    {
        private readonly INotificationService notificationService;

        public BracketNotifier(
            IUnitOfWorkFactory unitOfWorkFactory,
            INotificationService notificationService,
            IDiscordNotificationService discordNotificationService)
            : base(unitOfWorkFactory, discordNotificationService)
        {
            this.notificationService = notificationService;
        }

        /// <summary>
        /// "Tournament is now live": Expo push to every participant (moved from
        /// BracketService.SendNotification) + Discord announcement to the hub channel.
        /// </summary>
        public async Task TournamentStarted(TournamentEntity tournament, Guid tournamentId)
        {
            try
            {
                await SendTournamentLivePush(tournament, tournamentId);

                var hub = await GetDiscordTargetAsync(tournament.HubId, s => s.TournamentStarted);
                if (hub != null)
                    SendToDiscord(hub.DiscordWebhookUrl!, new AnnouncementCardData
                    {
                        Kind = AnnouncementKind.TournamentStarted,
                        HubName = hub.Name,
                        TournamentName = tournament.Name,
                    });
            }
            catch { /* notifications must never break bracket generation */ }
        }

        // F109 (moved from BracketService): participant ids + push tokens are resolved here (awaited,
        // while the request-scoped DbContext is alive); only the push send is fired-and-forgotten.
        private async Task SendTournamentLivePush(TournamentEntity tournament, Guid tournamentId)
        {
            var userIds = await this.AppUnitOfWork.TournamentParticipantRepository.GetAllUserIdsByTournamentId(tournamentId);
            if (userIds.Count == 0) return;

            var pushTokens = await this.AppUnitOfWork.UserRepository.GetPushTokensByUserIds(userIds);
            if (pushTokens.Count == 0) return;

            var title = tournament.Name;

            _ = Task.Run(async () =>
            {
                try
                {
                    await notificationService.SendToManyAsync(
                        pushTokens,
                        $"{title}",
                        $"Tournament is now live. Good luck!",
                        new { tournamentId });
                }
                catch { /* fire-and-forget */ }
            });
        }
    }
}
