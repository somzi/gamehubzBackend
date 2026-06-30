namespace GameHubz.Logic.Interfaces
{
    public interface IMatchChatRepository : IRepository<MatchChatEntity>
    {
        /// <summary>
        /// Match chat history, oldest→newest. When <paramref name="take"/> is null the full
        /// history is returned (legacy behaviour); when set, only the most recent
        /// <paramref name="take"/> messages older than <paramref name="before"/> are returned
        /// (paging cursor for "load earlier").
        /// </summary>
        Task<List<ChatMessageDto>> GetByMatchId(Guid matchId, int? take = null, DateTime? before = null);

        /// <summary>
        /// For the given matches, returns the number of messages sent by someone other
        /// than <paramref name="userId"/> after that user's read cursor (no cursor =
        /// every other-authored message counts). Only matches with at least one unread
        /// message appear in the result.
        /// </summary>
        Task<Dictionary<Guid, int>> GetUnreadCountsByMatch(List<Guid> matchIds, Guid userId);
    }
}