namespace GameHubz.Logic.Interfaces
{
    public interface ITeamMatchRepository : IRepository<TeamMatchEntity>
    {
        Task<TeamMatchEntity?> GetByIdWithSubMatches(Guid teamMatchId);

        Task<List<TeamMatchEntity>> GetByStageId(Guid stageId);
    }
}
