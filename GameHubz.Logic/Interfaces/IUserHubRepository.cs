using GameHubz.DataModels.Enums;

namespace GameHubz.Logic.Interfaces
{
    public interface IUserHubRepository : IRepository<UserHubEntity>
    {
        Task<UserHubEntity> GetByUserAndHub(Guid userId, Guid hubId);

        Task<UserHubEntity?> FindByUserAndHub(Guid userId, Guid hubId);

        /// <summary>
        /// Whether the user has ever had a membership row in the hub — current, or soft-deleted by an
        /// unfollow, a removal or a ban.
        /// </summary>
        Task<bool> HasMembershipHistory(Guid userId, Guid hubId);

        Task<HubRole?> GetRole(Guid userId, Guid hubId);

        Task<List<Guid>> GetHubIdsWithExclusiveAccess(Guid userId);

        Task<List<Guid>> GetManagedHubIds(Guid userId);

        Task<List<Guid>> GetManagerUserIds(Guid hubId);

        Task<List<UserHubOverview>> GetUsersByHub(Guid hubId);

        Task<List<UserHubOverview>> GetUsersByHubPaged(Guid hubId, int pageNumber, int pageSize, string? search);

        Task<List<UserHubOverview>> GetSwapCandidatesPaged(
            Guid hubId,
            int pageNumber,
            int pageSize,
            string? search,
            List<Guid> excludeUserIds,
            bool exclusiveAccessOnly,
            List<string>? countries,
            RegionType region);
    }
}