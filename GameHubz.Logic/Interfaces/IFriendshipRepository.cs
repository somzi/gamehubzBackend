namespace GameHubz.Logic.Interfaces
{
    public interface IFriendshipRepository : IRepository<FriendshipEntity>
    {
        Task<bool> AreFriends(Guid userAId, Guid userBId);

        Task<FriendshipEntity?> Find(Guid userAId, Guid userBId);

        // Bypasses the IsDeleted query filter so we can resurrect a soft-deleted
        // pair instead of inserting a duplicate (would violate UQ_Friendship_Pair).
        Task<FriendshipEntity?> FindIncludingDeleted(Guid userAId, Guid userBId);

        Task<List<FriendDto>> GetFriendsOf(Guid userId, string? search);
    }
}
