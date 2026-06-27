namespace GameHubz.Logic.Interfaces
{
    public interface IMatchChatReadRepository : IRepository<MatchChatReadEntity>
    {
        /// <summary>
        /// Upserts the caller's read cursor for a match to "now". Caller saves.
        /// </summary>
        Task MarkRead(Guid matchId, Guid userId, IUserContextReader userContextReader);
    }
}
