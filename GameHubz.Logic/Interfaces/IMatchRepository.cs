namespace GameHubz.Logic.Interfaces
{
    public interface IMatchRepository : IRepository<MatchEntity>
    {
        Task<PlayerStatsDto> GetStatsByUserId(Guid userId);

        Task<List<MatchListItemDto>> GetLastMatchesByUserId(Guid userId);

        Task<MatchEntity?> GetWithStage(Guid userId);

        Task<MatchEntity?> GetWithTournamentStage(Guid id);

        Task<bool> IsExistingByStageId(Guid? id);

        Task<bool> HasMatchesForStage(Guid value);

        Task<List<MatchOverviewDto>> GetByUser(Guid userId);
    }
}