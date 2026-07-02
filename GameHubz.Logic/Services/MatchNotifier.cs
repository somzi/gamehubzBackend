namespace GameHubz.Logic.Services
{
    /// <summary>
    /// Facade for match-level notifications (see <see cref="TournamentNotifier"/> for the pattern).
    /// All three events are Discord-only — none of them ever had an Expo push and this phase doesn't
    /// add one. The tournament + hub are resolved in ONE query and the (cheap) participant-name
    /// lookups only run once the webhook is known to be configured and the event enabled.
    /// </summary>
    public class MatchNotifier : DiscordNotifierBase
    {
        public MatchNotifier(
            IUnitOfWorkFactory unitOfWorkFactory,
            IDiscordNotificationService discordNotificationService,
            DiscordEmbedBuilder embedBuilder)
            : base(unitOfWorkFactory, discordNotificationService, embedBuilder)
        {
        }

        /// <summary>A proposed result was approved and committed to the bracket.</summary>
        public async Task MatchApproved(MatchEntity match, int homeScore, int awayScore)
        {
            try
            {
                var context = await GetMatchDiscordContextAsync(match.TournamentId, s => s.MatchApproved);
                if (context == null) return;

                var (tournament, hub) = context.Value;
                var (homeName, awayName) = await ResolveSideNamesAsync(match);
                SendToDiscord(hub.DiscordWebhookUrl!, this.EmbedBuilder.MatchApproved(hub.Name, tournament.Name, homeName, awayName, homeScore, awayScore));
            }
            catch { /* notifications must never break result approval */ }
        }

        /// <summary>A completed result was deleted and the match reopened. Scores are the pre-revert ones.</summary>
        public async Task MatchReverted(MatchEntity match, int? homeScore, int? awayScore)
        {
            try
            {
                var context = await GetMatchDiscordContextAsync(match.TournamentId, s => s.MatchReverted);
                if (context == null) return;

                var (tournament, hub) = context.Value;
                var (homeName, awayName) = await ResolveSideNamesAsync(match);
                SendToDiscord(hub.DiscordWebhookUrl!, this.EmbedBuilder.MatchReverted(hub.Name, tournament.Name, homeName, awayName, homeScore, awayScore));
            }
            catch { /* notifications must never break a result revert */ }
        }

        /// <summary>
        /// Admin closed an unplayed match with no winner (both sides no-show). Governed by the same
        /// "matchReverted" switch — both are admin corrections to a match, and the settings JSON
        /// deliberately stays at six switches.
        /// </summary>
        public async Task DoubleWalkover(MatchEntity match)
        {
            try
            {
                var context = await GetMatchDiscordContextAsync(match.TournamentId, s => s.MatchReverted);
                if (context == null) return;

                var (tournament, hub) = context.Value;
                var (homeName, awayName) = await ResolveSideNamesAsync(match);
                SendToDiscord(hub.DiscordWebhookUrl!, this.EmbedBuilder.DoubleWalkover(hub.Name, tournament.Name, homeName, awayName));
            }
            catch { /* notifications must never break a double walkover */ }
        }

        private async Task<(TournamentEntity tournament, HubEntity hub)?> GetMatchDiscordContextAsync(
            Guid tournamentId,
            Func<DiscordNotificationSettings, bool> eventEnabled)
        {
            var tournament = await this.AppUnitOfWork.TournamentRepository.GetWithHubById(tournamentId);
            var hub = tournament?.Hub;
            if (tournament == null || hub == null || string.IsNullOrWhiteSpace(hub.DiscordWebhookUrl)) return null;
            if (!eventEnabled(DiscordNotificationSettings.Parse(hub.DiscordNotificationSettings))) return null;

            return (tournament, hub);
        }

        private async Task<(string homeName, string awayName)> ResolveSideNamesAsync(MatchEntity match)
        {
            var homeName = await ResolveSideNameAsync(match.HomeUserId, match.HomeParticipant);
            var awayName = await ResolveSideNameAsync(match.AwayUserId, match.AwayParticipant);
            return (homeName, awayName);
        }

        // Team participants show the team name; solo participants and team sub-matches (which carry
        // the player ids on the match columns, same convention as IsMatchParticipant) show the username.
        private async Task<string> ResolveSideNameAsync(Guid? matchUserId, TournamentParticipantEntity? participant)
        {
            if (participant?.Team != null) return participant.Team.TeamName;

            var userId = participant?.UserId ?? matchUserId;
            if (userId.HasValue)
            {
                var user = await this.AppUnitOfWork.UserRepository.GetById(userId.Value);
                if (!string.IsNullOrWhiteSpace(user?.Username)) return user!.Username;
            }

            return "Unknown";
        }
    }
}
