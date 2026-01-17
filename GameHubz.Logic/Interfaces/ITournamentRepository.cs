using GameHubz.DataModels.Enums;

namespace GameHubz.Logic.Interfaces
{
    public interface ITournamentRepository : IRepository<TournamentEntity>
    {
        Task<TournamentEntity?> GetWithParticipents(Guid id);

        Task<List<TournamentOverview>> GetByHubPaged(Guid hubId, TournamentStatus status, int page, int pageSize);

        Task<int> GetByHubCount(Guid hubId, TournamentStatus status);

        Task<List<TournamentOverview>> GetByHubsPaged(Guid userId, List<Guid> hubIds, TournamentUserStatus status, int page, int pageSize);

        Task<int> GetCountByHubs(Guid userId, List<Guid> hubIds, TournamentUserStatus filter);

        Task<TournamentEntity> GetWithPendingRegistration(Guid id);
    }
}