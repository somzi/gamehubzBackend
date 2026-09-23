namespace GameHubz.Logic.Interfaces
{
    public interface IMatchChatReadRepository : IRepository<MatchChatReadEntity>
    {
        /// <summary>
        /// Upserts the caller's read cursor for a match to "now". Caller saves.
        /// </summary>
        Task MarkRead(Guid matchId, Guid userId, IUserContextReader userContextReader);

        /// <summary>
        /// Upserts the caller's mute flag for a match. A row created purely to mute starts with an
        /// epoch read cursor, so muting never silently marks the thread read. Caller saves.
        /// </summary>
        Task SetMuted(Guid matchId, Guid userId, bool muted, IUserContextReader userContextReader);

        /// <summary>Matches this user has muted — subtracted from the aggregate unread badge.</summary>
        Task<List<Guid>> GetMutedMatchIds(Guid userId);

        /// <summary>Who has muted this match — skipped when a new message fans out.</summary>
        Task<List<Guid>> GetMutedUserIds(Guid matchId);

        /// <summary>
        /// Permanently deletes every read cursor on these matches, soft-deleted ones included. Runs at
        /// once, in the caller's transaction if one is open.
        /// </summary>
        Task<int> DeleteByMatchIds(IReadOnlyCollection<Guid> matchIds);
    }
}
