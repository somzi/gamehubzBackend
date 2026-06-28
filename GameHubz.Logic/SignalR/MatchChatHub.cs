using GameHubz.DataModels.Tokens;
using GameHubz.Logic.Interfaces;
using GameHubz.Logic.Services;
using GameHubz.Logic.Tokens;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace GameHubz.Logic.SignalR
{
    [Authorize]
    public class MatchChatHub : Hub
    {
        private readonly IUnitOfWorkFactory unitOfWorkFactory;
        private readonly AccessTokenReader accessTokenReader;
        private readonly TournamentAuthorizationService tournamentAuth;

        public MatchChatHub(
            IUnitOfWorkFactory unitOfWorkFactory,
            AccessTokenReader accessTokenReader,
            TournamentAuthorizationService tournamentAuth)
        {
            this.unitOfWorkFactory = unitOfWorkFactory;
            this.accessTokenReader = accessTokenReader;
            this.tournamentAuth = tournamentAuth;
        }

        // Frontend zove ovu metodu kad uđe na ekran meča
        public async Task JoinMatchGroup(string matchId)
        {
            // F94: only a participant of the match — or a tournament manager moderating it — may join
            // its chat group. Previously any anonymous client could join an arbitrary match id and
            // eavesdrop. The user comes from the validated token, never from the client.
            var user = await this.accessTokenReader.ReadFromClaimsPrincipal(Context.User!);

            if (!Guid.TryParse(matchId, out var matchGuid))
                throw new HubException("Invalid match id.");

            var match = await this.unitOfWorkFactory.CreateAppUnitOfWork()
                .MatchRepository.GetWithParticipants(matchGuid);

            if (match == null)
                throw new HubException("You are not a participant of this match.");

            // Participants join their own match chat; tournament managers (hub owner / hub admin /
            // platform admin) may also join any match they moderate — that's the whole point of the
            // admin-help escalation, where an admin steps into the players' chat to resolve a dispute.
            var allowed = IsMatchParticipant(match, user.UserId)
                || await this.tournamentAuth.CanManageTournamentAsync(match.TournamentId, user);
            if (!allowed)
                throw new HubException("You are not a participant of this match.");

            await Groups.AddToGroupAsync(Context.ConnectionId, matchId);
        }

        // Frontend zove ovo kad izađe sa ekrana (clean-up)
        public async Task LeaveMatchGroup(string matchId)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, matchId);
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
