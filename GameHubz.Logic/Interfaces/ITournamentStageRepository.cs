namespace GameHubz.Logic.Interfaces
{
    public interface ITournamentStageRepository : IRepository<TournamentStageEntity>
    {
        Task<TournamentStageEntity> GetByOrder(Guid tournamentId, int order);

        Task<TournamentStageEntity> GetByTournamentId(Guid tournamentId);

        Task<TournamentStageEntity?> GetWithGroupsAndMatches(Guid Id);
    }
}