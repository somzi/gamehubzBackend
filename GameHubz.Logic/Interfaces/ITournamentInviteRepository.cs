using GameHubz.DataModels.Enums;

namespace GameHubz.Logic.Interfaces
{
    public interface ITournamentInviteRepository : IRepository<TournamentInviteEntity>
    {
        /// <summary>The player's open invite to this tournament, if any (at most one — unique index).</summary>
        Task<TournamentInviteEntity?> GetPending(Guid tournamentId, Guid userId);

        /// <summary>The player's most recent invite to this tournament, whatever its status.</summary>
        Task<TournamentInviteEntity?> GetLatest(Guid tournamentId, Guid userId);

        /// <summary>Whether the player ever declined an invite to this tournament — that blocks a new one.</summary>
        Task<bool> HasDeclined(Guid tournamentId, Guid userId);

        /// <summary>For each given player: Pending if they hold an open invite, else Declined if they ever declined.</summary>
        Task<Dictionary<Guid, TournamentInviteStatus>> GetInviteStates(Guid tournamentId, List<Guid> userIds);

        /// <summary>Every invite of the tournament, newest first, with the names the organizer's list shows.</summary>
        Task<List<TournamentInviteDto>> GetForTournament(Guid tournamentId);

        /// <summary>
        /// Moves an invite from one status to another only if it is still in <paramref name="from"/> —
        /// a conditional UPDATE, so a cancel racing an accept (or a double tap) can't both win.
        /// </summary>
        Task<bool> TryTransition(Guid inviteId, TournamentInviteStatus from, TournamentInviteStatus to, DateTime now, Guid? modifiedBy);

        /// <summary>Expires the other open swap invites for the same spot once one of them was accepted.</summary>
        Task<int> ExpireOpenSwapInvites(Guid tournamentId, Guid outgoingUserId, Guid exceptInviteId, DateTime now);

        /// <summary>Of the given players, those with an Exclusive-or-higher role in the hub.</summary>
        Task<HashSet<Guid>> GetUserIdsWithExclusiveAccess(Guid hubId, List<Guid> userIds);

        /// <summary>
        /// One page of players the organizer can pick from: hub members when there is no search,
        /// otherwise everyone whose username matches. Current entrants, banned players and the caller
        /// are left out in SQL so paging has no holes. With <paramref name="onlyEligible"/> the
        /// tournament's country / region / exclusive scope is applied too.
        /// </summary>
        Task<List<UserEntity>> GetCandidatesPaged(
            Guid tournamentId,
            Guid hubId,
            Guid callerUserId,
            string? search,
            bool onlyEligible,
            List<string>? countries,
            RegionType region,
            bool exclusive,
            int pageNumber,
            int pageSize);
    }
}
