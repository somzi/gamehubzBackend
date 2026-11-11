using GameHubz.DataModels.Enums;

namespace GameHubz.Logic.Interfaces
{
    public interface IMatchStreamRepository : IRepository<MatchStreamEntity>
    {
        // The current stream row for a match, regardless of streamer: a live row first, else the newest.
        Task<MatchStreamEntity?> GetLatestByMatchId(Guid matchId);

        // All stream rows for a match, current first — each streamer's live row, then newest by
        // (StartedAt, Id). The caller groups by streamer and takes the first of each for the current set.
        Task<List<MatchStreamEntity>> GetByMatchId(Guid matchId);

        // The current stream row of one streamer on a match (live first, else the newest) — both
        // opponents can stream at once.
        Task<MatchStreamEntity?> GetLatestByMatchAndStreamer(Guid matchId, Guid streamerUserId);

        // The in-app broadcast row a phone's session token belongs to.
        Task<MatchStreamEntity?> GetByBroadcastTokenHash(string tokenHash);

        // Stamps a heartbeat straight in the database (no tracked entity), only while the row is still live.
        // Returns false when the row has ended or been removed in the meantime.
        Task<bool> TouchHeartbeat(Guid streamId, DateTime now);

        // Leaves only the newest of the streamer's live rows on the match — newest by (StartedAt, Id), the
        // same order the reads below use — and ends the rest. Run after every activation: whichever order
        // concurrent activations insert in, at least one of these statements sees all their rows, and
        // none can end the true newest (a statement that sees fewer rows only ends rows older than one
        // it sees), so exactly one session stays live.
        Task KeepNewestLiveRow(Guid matchId, Guid streamerUserId, DateTime now);

        // Puts a broadcast session on air: inserts its live row unless a row already holds the session
        // token. The unique index on BroadcastTokenHash makes this the single point where activation
        // and stop can race — whichever inserts first wins. True when this call inserted the row.
        Task<bool> TryInsertLiveBroadcast(
            Guid id, Guid matchId, Guid streamerUserId, SocialType platform, string channelHandle, string tokenHash, DateTime now);

        // The stop's claim on a session that never went live: a hidden (soft-deleted), ended row holding
        // the token, so a heartbeat arriving after it can't put the session on air. False when a row
        // already holds the token — the session went live first and has to be ended instead.
        Task<bool> TryClaimStoppedBroadcast(
            Guid id, Guid matchId, Guid streamerUserId, SocialType platform, string channelHandle, string tokenHash, DateTime now);
    }
}
