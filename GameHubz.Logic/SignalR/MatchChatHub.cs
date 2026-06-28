using GameHubz.DataModels.Tokens;
using GameHubz.Logic.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace GameHubz.Logic.SignalR
{
    [Authorize]
    public class MatchChatHub : Hub
    {
        private readonly IUnitOfWorkFactory unitOfWorkFactory;

        public MatchChatHub(IUnitOfWorkFactory unitOfWorkFactory)
        {
            this.unitOfWorkFactory = unitOfWorkFactory;
        }

        // Frontend zove ovu metodu kad uđe na ekran meča
        public async Task JoinMatchGroup(string matchId)
        {
            // F94: only a participant of the match may join its chat group. Previously any anonymous
            // client could join an arbitrary match id and eavesdrop on its messages. The user id comes
            // from the validated token, never from the client.
            var userId = GetUserId();

            if (!Guid.TryParse(matchId, out var matchGuid))
                throw new HubException("Invalid match id.");

            var match = await this.unitOfWorkFactory.CreateAppUnitOfWork()
                .MatchRepository.GetWithParticipants(matchGuid);

            if (match == null || !IsMatchParticipant(match, userId))
                throw new HubException("You are not a participant of this match.");

            await Groups.AddToGroupAsync(Context.ConnectionId, matchId);
        }

        // Frontend zove ovo kad izađe sa ekrana (clean-up)
        public async Task LeaveMatchGroup(string matchId)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, matchId);
        }

        private Guid GetUserId()
        {
            var idClaim = Context.User?.FindFirst(JwtClaimIdentifiers.Id)?.Value;
            if (!Guid.TryParse(idClaim, out var userId))
                throw new HubException("Unauthenticated.");

            return userId;
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
    }
}
