using GameHubz.DataModels.Models;

namespace GameHubz.Logic.Interfaces
{
    public interface IUserHubRequestRepository : IRepository<UserHubRequestEntity>
    {
        Task<List<UserHubRequestDto>> GetPendingRequestsByHubId(Guid hubId);
        Task<UserHubRequestEntity?> GetPendingByHubAndUser(Guid hubId, Guid userId);
        Task<UserHubRequestEntity?> GetByIdWithHub(Guid requestId);
        Task<bool> HasPendingRequest(Guid hubId, Guid userId);
        Task<int> CountPendingByHubId(Guid hubId);
    }
}
