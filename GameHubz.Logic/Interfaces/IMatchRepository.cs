namespace GameHubz.Logic.Interfaces
{
    public interface IMatchRepository : IRepository<MatchEntity>
    {
        Task<PlayerStatsDto> GetStatsByUserId(Guid userId);

        Task<List<MatchListItemDto>> GetLastMatchesByUserId(Guid userId);

        Task<MatchEntity?> GetWithStage(Guid userId);
    }
}