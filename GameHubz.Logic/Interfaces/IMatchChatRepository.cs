namespace GameHubz.Logic.Interfaces
{
    public interface IMatchChatRepository : IRepository<MatchChatEntity>
    {
        Task<List<ChatMessageDto>> GetByMatchId(Guid matchId);

        /// <summary>
        /// For the given matches, returns the number of messages sent by someone other
        /// than <paramref name="userId"/> after that user's read cursor (no cursor =
        /// every other-authored message counts). Only matches with at least one unread
        /// message appear in the result.
        /// </summary>
        Task<Dictionary<Guid, int>> GetUnreadCountsByMatch(List<Guid> matchIds, Guid userId);
    }
}