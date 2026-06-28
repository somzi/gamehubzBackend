using FluentValidation;
using GameHubz.DataModels.Enums;
using GameHubz.Logic.SignalR;
using Microsoft.AspNetCore.SignalR;

namespace GameHubz.Logic.Services
{
    public class MatchChatService : AppBaseServiceGeneric<MatchChatEntity, MatchChatDto, MatchChatPost, MatchChatEdit>
    {
        private readonly IHubContext<MatchChatHub> hubContext;
        private readonly INotificationService notificationService;
        private readonly BadgeService badgeService;

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
            BadgeService badgeService) : base(
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
        }

        public async Task<ChatMessageDto> SendMessage(Guid matchId, string content)
        {
            var user = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();

            var match = await this.AppUnitOfWork.MatchRepository.GetWithParticipants(matchId);
            if (match == null) throw new BusinessRuleException("Match not found");

            // F33: only a participant of this match may post to its chat.
            if (!IsMatchParticipant(match, user.UserId))
                throw new UnauthorizedAccessToServiceException(this.LocalizationService);

            // Completed matches keep their chat history visible but read-only.
            if (match.Status == MatchStatus.Completed)
                throw new BusinessRuleException("Chat is closed for completed matches");

            var entity = new MatchChatEntity
            {
                MatchId = matchId,
                UserId = user.UserId,
                Content = content,
            };

            await this.AppUnitOfWork.MatchChatRepository.AddEntity(entity, this.UserContextReader);
            await this.SaveAsync();

            var dto = new ChatMessageDto
            {
                Id = entity.Id!.Value,
                UserId = user.UserId,
                UserNickname = user.Username,
                Content = content,
                SentAt = entity.CreatedOn!.Value
            };

            await hubContext.Clients.Group(matchId.ToString())
                             .SendAsync("ReceiveMessage", dto);

            // Push notification to the opponent (data resolved in-scope; see NotifyOpponentAsync).
            await NotifyOpponentAsync(matchId, match, content, user);

            return dto;
        }

        // F109: the opponent, badge bump and push token are all resolved here while the request-scoped
        // DbContext is alive; only the push send itself is fired-and-forgotten. The old version queried
        // this.AppUnitOfWork inside Task.Run, racing against the disposed request-scoped context.
        private async Task NotifyOpponentAsync(Guid matchId, MatchEntity match, string content, TokenUserInfo user)
        {
            Guid? opponentUserId = match.HomeUserId == user.UserId
                ? match.AwayUserId
                : match.HomeUserId;

            if (opponentUserId == null)
            {
                opponentUserId = match.HomeParticipant?.UserId == user.UserId
                    ? match.AwayParticipant?.UserId
                    : match.HomeParticipant?.UserId;
            }

            if (opponentUserId == null) return;

            // Live badge bump for the opponent (unread match chat) — before the
            // push-token early-out so it fires even when push isn't configured.
            await this.badgeService.PushAsync(opponentUserId.Value);

            var opponent = await this.AppUnitOfWork.UserRepository.GetById(opponentUserId.Value);
            if (string.IsNullOrEmpty(opponent?.PushToken)) return;

            var token = opponent.PushToken!;
            var tournamentId = match.TournamentId.ToString();

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
                catch { /* fire-and-forget – swallow errors */ }
            });
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

        public async Task<List<ChatMessageDto>> GetHistory(Guid matchId)
        {
            return await this.AppUnitOfWork.MatchChatRepository.GetByMatchId(matchId);
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

        protected override IRepository<MatchChatEntity> GetRepository()
            => this.AppUnitOfWork.MatchChatRepository;
    }
}