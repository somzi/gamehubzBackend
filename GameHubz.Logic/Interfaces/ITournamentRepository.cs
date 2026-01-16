using GameHubz.DataModels.Enums;

namespace GameHubz.Logic.Interfaces
{
    public interface ITournamentRepository : IRepository<TournamentEntity>
    {
        Task<TournamentEntity?> GetWithParticipents(Guid id);

        Task<List<TournamentEntity>> GetByHubPaged(Guid hubId, TournamentStatus status, int page, int pageSize);

        Task<int> GetByHubCount(Guid hubId, TournamentStatus status);
    }
}