using GameHubz.DataModels.Enums;

namespace GameHubz.Logic.Interfaces
{
    public interface ITournamentRepository : IRepository<TournamentEntity>
    {
        Task<TournamentEntity?> GetWithParticipents(Guid id);

        Task<List<TournamentOverview>> GetByHubPaged(Guid hubId, TournamentStatus status, int page, int pageSize);

        Task<int> GetByHubCount(Guid hubId, TournamentStatus status);

        Task<List<TournamentOverview>> GetByHubsPaged(Guid userId, List<Guid> hubIds, List<Guid> exclusiveHubIds, TournamentUserStatus status, RegionType region, string? userCountry, int page, int pageSize);

        Task<int> GetCountByHubs(Guid userId, List<Guid> hubIds, List<Guid> exclusiveHubIds, RegionType region, string? userCountry, TournamentUserStatus filter);

        Task<TournamentEntity> GetWithPendingRegistration(Guid id);

        Task<TournamentEntity?> GetWithFullDetails(Guid tournamentId);

        Task<TournamentOverview?> GetOverview(Guid tournamentId);

        Task<int> GetNumberOfTournamentsWonByUserId(Guid id);

        Task<int> GetChampionsCountByHubId(Guid hubId);

        Task<HubNextTournamentDto?> GetNextTournamentByHubId(Guid hubId);

        Task<HubLatestChampionDto?> GetLatestChampionByHubId(Guid hubId);

        Task<bool> CheckIsUserIsRegistered(Guid id, Guid userId);

        Task<TournamentEntity> GetWithHubById(Guid tournamentId);

        Task<Guid?> GetHubOwnerUserId(Guid tournamentId);

        Task<HubOwnershipInfo?> GetHubOwnership(Guid tournamentId);

        Task<TournamentApprovalContext?> GetApprovalContext(Guid tournamentId);

        Task<bool> TryClaimBracketGeneration(Guid tournamentId);

        Task RestoreBracketGenerationClaim(Guid tournamentId, TournamentStatus previousStatus);

        Task AcquireAdvancementLock(Guid tournamentId);

        Task ReleaseAdvancementLock(Guid tournamentId);
    }
}