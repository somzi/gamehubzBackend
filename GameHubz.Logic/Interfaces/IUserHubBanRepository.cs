namespace GameHubz.Logic.Interfaces
{
    public interface IUserHubBanRepository : IRepository<UserHubBanEntity>
    {
        Task<bool> IsBanned(Guid userId, Guid hubId);

        Task<UserHubBanEntity?> FindActiveBan(Guid userId, Guid hubId);

        Task<List<HubBanOverview>> GetBansByHub(Guid hubId);
    }
}
