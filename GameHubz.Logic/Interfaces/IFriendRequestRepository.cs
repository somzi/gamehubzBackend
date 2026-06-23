using GameHubz.DataModels.Enums;

namespace GameHubz.Logic.Interfaces
{
    public interface IFriendRequestRepository : IRepository<FriendRequestEntity>
    {
        Task<FriendRequestEntity?> FindPendingBetween(Guid userAId, Guid userBId);

        Task<List<FriendRequestDto>> GetIncomingPending(Guid userId, string? search);

        Task<List<FriendRequestDto>> GetOutgoingPending(Guid userId, string? search);

        Task<int> GetIncomingPendingCount(Guid userId);
    }
}
