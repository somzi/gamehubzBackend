namespace GameHubz.Logic.Interfaces
{
    public interface IUserHubRepository : IRepository<UserHubEntity>
    {
        Task<UserHubEntity> GetByUserAndHub(Guid userId, Guid hubId);

        Task<List<UserHubOverview>> GetUsersByHub(Guid hubId);
    }
}