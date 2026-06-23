using GameHubz.DataModels.Enums;
using GameHubz.Logic.SignalR;
using Microsoft.AspNetCore.SignalR;

namespace GameHubz.Logic.Services
{
    /// <summary>
    /// Computes the signed-in user's aggregate unread / pending counters (friend requests,
    /// unread DMs, unread match chat, matches awaiting scheduling) and pushes them live to
    /// the user's devices through the <see cref="UserHub"/> whenever an underlying count changes.
    /// </summary>
    public class BadgeService : AppBaseService
    {
        private readonly IHubContext<UserHub> hubContext;

        public BadgeService(
            IUnitOfWorkFactory factory,
            IUserContextReader userContextReader,
            ILocalizationService localizationService,
            IHubContext<UserHub> hubContext)
            : base(factory.CreateAppUnitOfWork(), userContextReader, localizationService)
        {
            this.hubContext = hubContext;
        }

        public async Task<BadgeCountsDto> GetMyBadgesAsync()
        {
            var user = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();
            return await ComputeAsync(user.UserId);
        }

        public async Task<BadgeCountsDto> ComputeAsync(Guid userId)
        {
            var friendRequests = await this.AppUnitOfWork.FriendRequestRepository.GetIncomingPendingCount(userId);
            var unreadDms = await this.AppUnitOfWork.DirectMessageRepository.GetUnreadCountForUser(userId);

            var activeMatches = await this.AppUnitOfWork.MatchRepository.GetActiveForUserBadge(userId);
            var matchIds = activeMatches.Select(m => m.Id).ToList();
            var unreadByMatch = await this.AppUnitOfWork.MatchChatRepository.GetUnreadCountsByMatch(matchIds, userId);

            return new BadgeCountsDto
            {
                FriendRequests = friendRequests,
                UnreadDirectMessages = unreadDms,
                UnreadMatchMessages = unreadByMatch.Values.Sum(),
                MatchesWithUnreadChat = unreadByMatch.Count,
                MatchesToSchedule = activeMatches.Count(m => m.Status == MatchStatus.Pending),
            };
        }

        /// <summary>
        /// Recomputes badges for a user and pushes them to their UserHub group. Best-effort:
        /// failures are swallowed so a notification problem never breaks the triggering action.
        /// </summary>
        public async Task PushAsync(Guid userId)
        {
            try
            {
                var dto = await ComputeAsync(userId);
                await this.hubContext.Clients
                    .Group(UserHub.GroupName(userId))
                    .SendAsync("BadgesUpdated", dto);
            }
            catch
            {
                // best-effort — never let a badge push break the underlying mutation
            }
        }
    }
}
