namespace GameHubz.Logic.Interfaces
{
    public interface IUserBlockRepository : IRepository<UserBlockEntity>
    {
        Task<bool> IsBlocked(Guid blockerId, Guid blockedId);

        Task<bool> EitherBlocks(Guid userAId, Guid userBId);

        Task<UserBlockEntity?> Find(Guid blockerId, Guid blockedId);

        // Bypasses the IsDeleted query filter so we can resurrect a soft-deleted
        // pair instead of inserting a duplicate (would violate UQ_UserBlock_Pair).
        Task<UserBlockEntity?> FindIncludingDeleted(Guid blockerId, Guid blockedId);

        Task<List<BlockedUserDto>> GetBlockedList(Guid blockerId, string? search);
    }
}
