using GameHubz.DataModels.Enums;

namespace GameHubz.Logic.Interfaces
{
    public interface IUserHubRepository : IRepository<UserHubEntity>
    {
        Task<UserHubEntity> GetByUserAndHub(Guid userId, Guid hubId);

        Task<UserHubEntity?> FindByUserAndHub(Guid userId, Guid hubId);

        Task<HubRole?> GetRole(Guid userId, Guid hubId);

        Task<List<Guid>> GetHubIdsWithExclusiveAccess(Guid userId);

        Task<List<Guid>> GetManagedHubIds(Guid userId);

        Task<List<UserHubOverview>> GetUsersByHub(Guid hubId);

        Task<List<UserHubOverview>> GetUsersByHubPaged(Guid hubId, int pageNumber, int pageSize, string? search);
    }
}