using GameHubz.DataModels.Enums;

namespace GameHubz.Logic.Services
{
    /// <summary>
    /// Facade for tournament-lifecycle notifications. Each method fans one domain event out to both
    /// channels: the existing Expo push (when the event has one) and the hub's Discord webhook (when
    /// configured and enabled for the event). Services call these methods only — they never talk to
    /// <see cref="INotificationService"/> / <see cref="IDiscordNotificationService"/> directly for
    /// these public hub events. Every method swallows its own failures: a notification must never
    /// break the flow that triggered it.
    /// </summary>
    public class TournamentNotifier : DiscordNotifierBase
    {
        private readonly INotificationService notificationService;

        public TournamentNotifier(
            IUnitOfWorkFactory unitOfWorkFactory,
            INotificationService notificationService,
            IDiscordNotificationService discordNotificationService,
            DiscordEmbedBuilder embedBuilder)
            : base(unitOfWorkFactory, discordNotificationService, embedBuilder)
        {
            this.notificationService = notificationService;
        }

        /// <summary>
        /// "Registration is open": Expo push to the hub's members (moved from
        /// TournamentService.SendNotification) + Discord announcement to the hub channel.
        /// </summary>
        public async Task RegistrationOpened(TournamentDto model)
        {
            try
            {
                await SendRegistrationOpenedPush(model);

                var hub = await GetDiscordTargetAsync(model.HubId, s => s.RegistrationOpened);
                if (hub != null)
                    SendToDiscord(hub.DiscordWebhookUrl!, this.EmbedBuilder.RegistrationOpened(hub.Name, model.Name));
            }
            catch { /* notifications must never break tournament creation */ }
        }

        /// <summary>
        /// Discord-only: closing registration has never had an Expo push and this phase doesn't add one.
        /// </summary>
        public async Task RegistrationClosed(TournamentEntity tournament)
        {
            try
            {
                var hub = await GetDiscordTargetAsync(tournament.HubId, s => s.RegistrationClosed);
                if (hub == null) return;

                int participantCount = tournament.TournamentParticipants?.Count ?? 0;
                SendToDiscord(hub.DiscordWebhookUrl!, this.EmbedBuilder.RegistrationClosed(hub.Name, tournament.Name, participantCount));
            }
            catch { /* notifications must never break closing registration */ }
        }

        /// <summary>
        /// Public "Tournament Finished" announcement to the hub's Discord channel. Separate from the
        /// personal "you won" Expo push (BracketService.NotifyTournamentWinnerAsync), which stays
        /// Expo-only. Champion only — runner-up / third place aren't cheaply available at the
        /// completion call sites and this must not add expensive standings queries.
        /// </summary>
        public async Task TournamentFinished(TournamentEntity tournament)
        {
            try
            {
                var hub = await GetDiscordTargetAsync(tournament.HubId, s => s.TournamentFinished);
                if (hub == null) return;

                var championName = await ResolveChampionNameAsync(tournament);
                SendToDiscord(hub.DiscordWebhookUrl!, this.EmbedBuilder.TournamentFinished(hub.Name, tournament.Name, championName));
            }
            catch { /* never let a Discord announcement break tournament completion */ }
        }

        private async Task<string?> ResolveChampionNameAsync(TournamentEntity tournament)
        {
            if (tournament.WinnerUserId.HasValue && tournament.WinnerUserId.Value != Guid.Empty)
            {
                var user = await this.AppUnitOfWork.UserRepository.GetById(tournament.WinnerUserId.Value);
                return user?.Username;
            }

            if (tournament.WinnerTeamId.HasValue)
            {
                var team = await this.AppUnitOfWork.TournamentTeamRepository.GetByIdWithMembers(tournament.WinnerTeamId.Value);
                return team?.TeamName;
            }

            return null;
        }

        // F109 (moved from TournamentService): resolve the recipients' push tokens here, while the
        // request-scoped DbContext is alive, then fire-and-forget ONLY the push send (which goes
        // through NotificationService's own scope).
        private async Task SendRegistrationOpenedPush(TournamentDto model)
        {
            var hubMembers = await this.AppUnitOfWork.UserHubRepository.GetUsersByHub(model.HubId!.Value);
            if (hubMembers == null || hubMembers.Count == 0) return;

            var pushTokens = hubMembers
                .Where(m => m.HubRole != HubRole.HubOwner && !string.IsNullOrEmpty(m.PushToken))
                // Exclusive tournaments are invisible to plain members, so don't notify them.
                .Where(m => !model.IsExclusive || m.HubRole == HubRole.HubAdmin || m.HubRole == HubRole.HubExclusive)
                .Select(m => m.PushToken!)
                .Distinct()
                .ToList();

            if (pushTokens.Count == 0) return;

            var title = model.Name;
            var tournamentId = model.Id;

            _ = Task.Run(async () =>
            {
                try
                {
                    await notificationService.SendToManyAsync(
                        pushTokens,
                        title,
                        "Registration is open, grab your spot!",
                        new { tournamentId });
                }
                catch { /* fire-and-forget */ }
            });
        }
    }
}
