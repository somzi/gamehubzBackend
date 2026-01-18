namespace GameHubz.Logic.Interfaces
{
    public interface IHubRepository : IRepository<HubEntity>
    {
        Task<List<HubEntity>> GetOverview();

        Task<List<HubEntity>> GetByUserId(Guid userId);

        Task<HubEntity> GetWithDetailsById(Guid id);
    }
}