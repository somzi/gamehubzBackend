namespace GameHubz.Logic.Interfaces
{
    public interface IHubRepository : IRepository<HubEntity>
    {
        Task<List<HubEntity>> GetOverview();

        Task<HubEntity> GetWithDetailsById(Guid id);
    }
}