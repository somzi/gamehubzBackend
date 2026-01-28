namespace GameHubz.Logic.Interfaces
{
    public interface IHubRepository : IRepository<HubEntity>
    {
        Task<List<HubDto>> GetOverview();

        Task<List<HubEntity>> GetByUserId(Guid userId);

        Task<HubOverviewDto?> GetOverviewDtoById(Guid hubId);

        Task<bool> IsUserFollowingHub(Guid userId, Guid id);

        Task<IEnumerable<HubDto>> GetHubsByUserId(Guid userId, bool joined);
    }
}