namespace GameHubz.Logic.Interfaces
{
    public interface IMatchStreamRepository : IRepository<MatchStreamEntity>
    {
        // Newest stream row for a match (latest regardless of streamer).
        Task<MatchStreamEntity?> GetLatestByMatchId(Guid matchId);

        // All stream rows for a match, newest first (caller groups by streamer for the current set).
        Task<List<MatchStreamEntity>> GetByMatchId(Guid matchId);

        // Newest stream row for a match owned by a specific streamer — both opponents can stream at once.
        Task<MatchStreamEntity?> GetLatestByMatchAndStreamer(Guid matchId, Guid streamerUserId);
    }
}
