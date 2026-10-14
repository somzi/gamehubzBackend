using GameHubz.DataModels.Enums;

namespace GameHubz.Logic.Interfaces
{
    public interface ITournamentRepository : IRepository<TournamentEntity>
    {
        Task<TournamentEntity?> GetWithParticipents(Guid id);

        Task<List<TournamentOverview>> GetByHubPaged(Guid hubId, TournamentStatus status, int page, int pageSize, bool includeUnscheduledDrafts);

        Task<int> GetByHubCount(Guid hubId, TournamentStatus status, bool includeUnscheduledDrafts);

        Task<List<TournamentOverview>> GetByHubsPaged(Guid userId, List<Guid> hubIds, List<Guid> exclusiveHubIds, TournamentUserStatus status, RegionType region, string? userCountry, int page, int pageSize, bool includeScheduled);

        Task<int> GetCountByHubs(Guid userId, List<Guid> hubIds, List<Guid> exclusiveHubIds, RegionType region, string? userCountry, TournamentUserStatus filter, bool includeScheduled);

        Task<TournamentEntity> GetWithPendingRegistration(Guid id);

        Task<TournamentEntity?> GetWithFullDetails(Guid tournamentId);

        Task<TournamentOverview?> GetOverview(Guid tournamentId);

        Task<int> GetNumberOfTournamentsWonByUserId(Guid id);

        Task<bool> CheckIsUserIsRegistered(Guid id, Guid userId);

        Task<bool> JoinCodeExists(string joinCode);

        Task<Guid?> GetIdByJoinCode(string joinCode);

        Task SetJoinCode(Guid tournamentId, string joinCode);

        Task<TournamentEntity> GetWithHubById(Guid tournamentId);

        Task<Guid?> GetHubOwnerUserId(Guid tournamentId);

        /// <summary>The tournament's HubId, also when that hub has been soft-deleted.</summary>
        Task<Guid?> GetHubId(Guid tournamentId);

        Task<HubOwnershipInfo?> GetHubOwnership(Guid tournamentId);

        Task<TournamentApprovalContext?> GetApprovalContext(Guid tournamentId);

        Task<bool> RequiresAvailabilityForChat(Guid tournamentId);

        Task<bool> TryClaimBracketGeneration(Guid tournamentId);

        Task RestoreBracketGenerationClaim(Guid tournamentId, TournamentStatus previousStatus);

        Task SetBracketSeedingMode(Guid tournamentId, BracketSeedingMode mode);

        Task AcquireAdvancementLock(Guid tournamentId);

        Task ReleaseAdvancementLock(Guid tournamentId);

        /// <summary>
        /// Serialises everything that adds a participant to the tournament (approving a sign-up,
        /// accepting an invite), so two players can't both take the last place. Transaction-scoped:
        /// call it inside ExecuteInTransactionAsync — the lock is released on commit or rollback.
        /// </summary>
        Task LockEntry(Guid tournamentId);

        /// <summary>
        /// Moves the tournament from <paramref name="expected"/> to <paramref name="newStatus"/> only if
        /// it is still in <paramref name="expected"/>, touching nothing but the status columns (and
        /// EndedOn, when <paramref name="markEnded"/>, only if unset). Returns whether it changed.
        /// </summary>
        Task<bool> TryChangeStatus(Guid tournamentId, TournamentStatus expected, TournamentStatus newStatus, bool markEnded, DateTime now, Guid? modifiedBy);

        /// <summary>
        /// For an edit staged with UpdateEntity: leaves the lifecycle columns (status, end, winner) out
        /// of the UPDATE, so an edit never writes back what it read — those only ever move through
        /// their own transitions.
        /// </summary>
        void KeepLifecycleColumns(TournamentEntity entity);
    }
}