using FluentValidation;
using GameHubz.DataModels.Config;
using GameHubz.DataModels.Enums;
using GameHubz.Logic.SignalR;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;

namespace GameHubz.Logic.Services
{
    public class MatchChatService : AppBaseServiceGeneric<MatchChatEntity, MatchChatDto, MatchChatPost, MatchChatEdit>
    {
        // Discord DM throttle for chat traffic: the first message in a match chat DMs the
        // opponent, then that match stays quiet for this window. Push notifications are
        // untouched — they still fire per message; only the additive Discord mirror is tamed.
        private static readonly TimeSpan ChatDmCooldown = TimeSpan.FromMinutes(10);

        private readonly IHubContext<MatchChatHub> hubContext;
        private readonly INotificationService notificationService;
        private readonly BadgeService badgeService;
        private readonly TournamentAuthorizationService tournamentAuth;
        private readonly IDiscordDmService discordDmService;
        private readonly ICacheService cacheService;
        private readonly ShareLinksConfig shareLinksConfig;

        public MatchChatService(
            IUnitOfWorkFactory factory,
            IMapper mapper,
            ILocalizationService localizationService,
            IValidator<MatchChatEntity> validator,
            SearchService searchService,
            ServiceFunctions serviceFunctions,
            IUserContextReader userContextReader,
            IHubContext<MatchChatHub> hubContext,
            INotificationService notificationService,
            BadgeService badgeService,
            TournamentAuthorizationService tournamentAuth,
            IDiscordDmService discordDmService,
            ICacheService cacheService,
            IOptions<ShareLinksConfig> shareLinksOptions) : base(
                factory.CreateAppUnitOfWork(),
                userContextReader,
                localizationService,
                searchService,
                validator,
                mapper,
                serviceFunctions)
        {
            this.hubContext = hubContext;
            this.notificationService = notificationService;
            this.badgeService = badgeService;
            this.tournamentAuth = tournamentAuth;
            this.discordDmService = discordDmService;
            this.cacheService = cacheService;
            this.shareLinksConfig = shareLinksOptions.Value;
        }

        public async Task<ChatMessageDto> SendMessage(Guid matchId, string content)
        {
            var user = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();

            var match = await this.AppUnitOfWork.MatchRepository.GetWithParticipants(matchId);
            if (match == null) throw new BusinessRuleException(this.LocalizationService["BusinessRule.MatchNotFound"]);

            // F33: only a participant of this match — or a tournament manager moderating it (hub
            // owner / hub admin / platform admin) — may post to its chat. Managers step in via the
            // admin-help escalation to talk the players through a dispute.
            if (!IsMatchParticipant(match, user.UserId)
                && !await this.tournamentAuth.CanManageTournamentAsync(match.TournamentId, user))
                throw new UnauthorizedAccessToServiceException(this.LocalizationService);

            // Completed matches keep their chat history visible but read-only.
            if (match.Status == MatchStatus.Completed)
                throw new BusinessRuleException(this.LocalizationService["BusinessRule.ChatClosedCompleted"]);

            var entity = new MatchChatEntity
            {
                MatchId = matchId,
                UserId = user.UserId,
                Content = content,
            };

            await this.AppUnitOfWork.MatchChatRepository.AddEntity(entity, this.UserContextReader);
            await this.SaveAsync();

            // Resolve the sender's display name + avatar so the live SignalR payload matches the
            // history projection (which now carries UserAvatarUrl). Without this an admin chiming in
            // arrives with no avatar and the client falls back to showing the opponent's.
            var sender = await this.AppUnitOfWork.UserRepository.GetById(user.UserId);

            var dto = new ChatMessageDto
            {
                Id = entity.Id!.Value,
                UserId = user.UserId,
                UserNickname = string.IsNullOrWhiteSpace(sender?.Nickname) ? user.Username : sender!.Nickname!,
                UserAvatarUrl = sender?.AvatarUrl,
                Content = content,
                SentAt = entity.CreatedOn!.Value
            };

            await hubContext.Clients.Group(matchId.ToString())
                             .SendAsync("ReceiveMessage", dto);

            // Push notification to everyone in the conversation (see NotifyChatRecipientsAsync).
            await NotifyChatRecipientsAsync(matchId, match, content, user);

            return dto;
        }

        // F109: the recipients, badge bumps and push tokens are all resolved here while the
        // request-scoped DbContext is alive; only the push sends themselves are fired-and-forgotten.
        //
        // Recipients are the whole conversation, not just the opponent: both sides of the match
        // PLUS anyone who has already posted in it. That second set is what keeps an organizer who
        // stepped in to mediate in the loop -- before it, an admin who wrote into a match chat never
        // heard about the replies, so the thread they opened went silent on them. Taking both sides
        // (rather than "the other one") also fixes the admin-sender case, where the old opponent
        // lookup resolved to a single player and left the other one uninformed.
        private async Task NotifyChatRecipientsAsync(Guid matchId, MatchEntity match, string content, TokenUserInfo user)
        {
            var recipientIds = new HashSet<Guid>();

            // Team sub-matches carry the player ids on the match; solo matches resolve through the
            // participants. A team participant row has no meaningful UserId -- hence the Empty guard.
            foreach (var side in new[]
            {
                match.HomeUserId ?? match.HomeParticipant?.UserId,
                match.AwayUserId ?? match.AwayParticipant?.UserId,
            })
            {
                if (side is { } sideUserId && sideUserId != Guid.Empty) recipientIds.Add(sideUserId);
            }

            // Snapshot of who the two players are, so the blanket "chats I moderate" switch below
            // can tell a player (never silenced by it) from an organizer who stepped in.
            var playerIds = new HashSet<Guid>(recipientIds);

            foreach (var authorId in await this.AppUnitOfWork.MatchChatRepository.GetChatUserIds(matchId))
                recipientIds.Add(authorId);

            recipientIds.Remove(user.UserId);

            // Per-thread mute wins over everything: no push, no DM, no badge for this match.
            foreach (var mutedId in await this.AppUnitOfWork.MatchChatReadRepository.GetMutedUserIds(matchId))
                recipientIds.Remove(mutedId);

            if (recipientIds.Count == 0) return;

            var tournamentId = match.TournamentId.ToString();

            foreach (var recipientId in recipientIds)
            {
                var recipient = await this.AppUnitOfWork.UserRepository.GetById(recipientId);
                if (recipient == null) continue;

                // Blanket opt-out for threads the recipient only moderates. Deliberately does not
                // touch their own matches: missing a scheduling message on a match you have to
                // play is a different kind of harm than one dispute chat being noisy.
                if (!playerIds.Contains(recipientId) && !recipient.ModeratedChatNotifications) continue;

                // Live badge bump -- before the push-token early-out so it fires even when push
                // isn't configured. For a moderator this counter is the only thing that surfaces
                // the thread in-app, so it must not depend on them having a device token.
                await this.badgeService.PushAsync(recipientId);

                if (!string.IsNullOrEmpty(recipient.PushToken))
                {
                    var token = recipient.PushToken!;
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await notificationService.SendToOneAsync(
                                token,
                                user.Username,
                                content,
                                new
                                {
                                    type = "matchMessage",
                                    matchId = matchId.ToString(),
                                    // Carried for team-tournament sub-matches so the mobile deep link can route
                                    // to the team-match modal (the solo modal renders empty for a sub-match id).
                                    teamMatchId = match.TeamMatchId?.ToString(),
                                    tournamentId,
                                });
                        }
                        catch { /* fire-and-forget - swallow errors */ }
                    });
                }

                // Additive Discord DM (push stays the primary channel) -- same trigger as the push.
                // Throttled per recipient per match (see ChatDmCooldown); checked here in the request
                // scope so the fire-and-forget send stays cache-free. Masked link keeps the raw URL
                // out of the message; the <> also suppresses Discord's link-preview embed.
                if (recipient.DiscordDmEnabled)
                {
                    string cooldownKey = $"discord:dm_chat_cooldown:{recipientId}:{matchId}";
                    if (await this.cacheService.GetAsync<string>(cooldownKey) == null)
                    {
                        await this.cacheService.SetAsync(cooldownKey, "1", ChatDmCooldown);
                        string body = content.Length > 120 ? content.Substring(0, 117) + "..." : content;
                        this.discordDmService.SendDmInBackground(
                            recipient.DiscordUserId,
                            $"💬 **{user.Username}** (match chat): {body}\n[Open in GameHubz](<{shareLinksConfig.BaseUrl}/tournament/{match.TournamentId}>)");
                    }
                }
            }
        }

        private static bool IsMatchParticipant(MatchEntity match, Guid userId)
        {
            // Team sub-matches carry the player ids on the match itself; solo matches use the participants.
            if (match.HomeUserId == userId || match.AwayUserId == userId) return true;

            return (match.HomeParticipant != null &&
                        (match.HomeParticipant.UserId == userId ||
                         match.HomeParticipant.Team?.Members.Any(m => m.UserId == userId) == true)) ||
                   (match.AwayParticipant != null &&
                        (match.AwayParticipant.UserId == userId ||
                         match.AwayParticipant.Team?.Members.Any(m => m.UserId == userId) == true));
        }

        public async Task<List<ChatMessageDto>> GetHistory(Guid matchId, int? take = null, DateTime? before = null)
        {
            var user = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();

            var match = await this.AppUnitOfWork.MatchRepository.GetWithParticipants(matchId);
            if (match == null) throw new BusinessRuleException(this.LocalizationService["BusinessRule.MatchNotFound"]);

            // The same trust boundary SendMessage enforces (F33). Closing the write path left the
            // read path open: a match id was enough for any signed-in user to pull the two sides'
            // whole conversation. Unlike SendMessage there is no Completed check — history stays
            // readable once the match is over, it just becomes read-only.
            //
            // 400 rather than the 401 SendMessage throws: the mobile client's response interceptor
            // treats 401 as an expired token and burns a refresh round-trip before giving up (and
            // logs the user out if that refresh happens to fail), which is the wrong reaction to a
            // permission answer that will not change.
            if (!IsMatchParticipant(match, user.UserId)
                && !await this.tournamentAuth.CanManageTournamentAsync(match.TournamentId, user))
                throw new BusinessRuleException(this.LocalizationService["Exception.UnauthorizedAccessToServiceException"]);

            return await this.AppUnitOfWork.MatchChatRepository.GetByMatchId(matchId, take, before);
        }

        /// <summary>
        /// Marks the match chat as read up to now for the caller and refreshes their badges.
        /// </summary>
        public async Task MarkRead(Guid matchId)
        {
            var user = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();

            await this.AppUnitOfWork.MatchChatReadRepository.MarkRead(matchId, user.UserId, this.UserContextReader);
            await this.SaveAsync();

            await this.badgeService.PushAsync(user.UserId);
        }

        /// <summary>Whether the caller has this match's chat muted.</summary>
        public async Task<bool> GetMuted(Guid matchId)
        {
            var user = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();

            return (await this.AppUnitOfWork.MatchChatReadRepository.GetMutedMatchIds(user.UserId))
                .Contains(matchId);
        }

        /// <summary>
        /// Mutes or unmutes this match's chat for the caller. Mute suppresses push, Discord DM and
        /// the aggregate badge; the thread keeps its place — and its real unread count — in the
        /// inbox. Anyone who can read the chat can mute it, so this needs no extra authorization
        /// beyond the controller's [Authorize]: it only ever writes the caller's own row.
        /// </summary>
        public async Task SetMuted(Guid matchId, bool muted)
        {
            var user = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();

            await this.AppUnitOfWork.MatchChatReadRepository.SetMuted(matchId, user.UserId, muted, this.UserContextReader);
            await this.SaveAsync();

            // Muting should drop the counter immediately rather than at the next poll.
            await this.badgeService.PushAsync(user.UserId);
        }

        protected override IRepository<MatchChatEntity> GetRepository()
            => this.AppUnitOfWork.MatchChatRepository;
    }
}