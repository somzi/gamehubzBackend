namespace GameHubz.Logic.Interfaces
{
    public interface IUserHubBanRepository : IRepository<UserHubBanEntity>
    {
        Task<bool> IsBanned(Guid userId, Guid hubId);

        Task<UserHubBanEntity?> FindActiveBan(Guid userId, Guid hubId);

        /// <summary>The ones among <paramref name="userIds"/> with an active ban in the hub.</summary>
        Task<List<Guid>> GetBannedUserIds(Guid hubId, IReadOnlyCollection<Guid> userIds);

        Task<List<HubBanOverview>> GetBansByHub(Guid hubId);
    }
}
