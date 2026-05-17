using GameHubz.DataModels.Enums;

namespace GameHubz.Logic.Interfaces
{
    public interface ITournamentRepository : IRepository<TournamentEntity>
    {
        Task<TournamentEntity?> GetWithParticipents(Guid id);

        Task<List<TournamentOverview>> GetByHubPaged(Guid hubId, TournamentStatus status, int page, int pageSize);

        Task<int> GetByHubCount(Guid hubId, TournamentStatus status);

        Task<List<TournamentOverview>> GetByHubsPaged(Guid userId, List<Guid> hubIds, TournamentUserStatus status, RegionType region, int page, int pageSize);

        Task<int> GetCountByHubs(Guid userId, List<Guid> hubIds, RegionType region, TournamentUserStatus filter);

        Task<TournamentEntity> GetWithPendingRegistration(Guid id);

        Task<TournamentEntity?> GetWithFullDetails(Guid tournamentId);

        Task<TournamentOverview?> GetOverview(Guid tournamentId);

        Task<int> GetNumberOfTournamentsWonByUserId(Guid id);

        Task<bool> CheckIsUserIsRegistered(Guid id, Guid userId);

        Task<TournamentEntity> GetWithHubById(Guid tournamentId);

        Task<Guid?> GetHubOwnerUserId(Guid tournamentId);
    }
}