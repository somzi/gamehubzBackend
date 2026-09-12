using GameHubz.Common.Interfaces;
using GameHubz.Data.Base;
using GameHubz.Data.Context;
using GameHubz.DataModels.Domain;
using GameHubz.Logic.Interfaces;
using GameHubz.Logic.Utility;
using Microsoft.EntityFrameworkCore;

namespace GameHubz.Data.Repository
{
    public class MatchChatReadRepository : BaseRepository<ApplicationContext, MatchChatReadEntity>, IMatchChatReadRepository
    {
        public MatchChatReadRepository(
            ApplicationContext context,
            DateTimeProvider dateTimeProvider,
            IFilterExpressionBuilder filterExpressionBuilder,
            ISortStringBuilder sortStringBuilder,
            ILocalizationService localizationService)
            : base(context, dateTimeProvider, filterExpressionBuilder, sortStringBuilder, localizationService)
        {
        }

        // Both writers below are atomic PostgreSQL upserts rather than the natural EF
        // "SELECT, then INSERT-or-UPDATE". Two reasons:
        //
        //  1. Concurrency. The mobile panel fires POST /read on open AND on every incoming
        //     SignalR message; on a slow link two of those can be in flight at once. Both
        //     requests saw "no row yet" and both INSERTed, and the second one died on
        //     UQ_MatchChatRead_Match_User (23505 -> DbUpdateException -> 500). A mute toggle
        //     racing a mark-read collided the same way.
        //  2. Soft delete. The entity carries a global IsDeleted == false query filter, but the
        //     unique constraint does not - so a soft-deleted row is invisible to the lookup yet
        //     still blocks the insert, which would 500 that (match, user) pair forever.
        //
        // ON CONFLICT settles both in one round trip; the DO UPDATE branch also revives a
        // soft-deleted cursor, which is the right recovery for a row that is bookkeeping
        // rather than user content.
        private const string UpsertConflictTarget = @"ON CONFLICT (""MatchId"", ""UserId"") DO UPDATE SET ";

        public async Task MarkRead(Guid matchId, Guid userId, IUserContextReader userContextReader)
        {
            // The read marker FKs to Match. A stale client can POST /read for a match that was
            // already deleted (e.g. a double-elim cascade delete), and the insert below would blow
            // up with a foreign-key violation (FK_MatchChatRead_Match -> 500). There is nothing to
            // mark read on a match that no longer exists, so silently no-op.
            bool matchExists = await this.ContextBase.Set<MatchEntity>()
                .AnyAsync(m => m.Id == matchId);
            if (!matchExists) return;

            var now = DateTime.UtcNow;

            await this.ContextBase.Database.ExecuteSqlRawAsync(
                @"INSERT INTO ""MatchChatRead""
                      (""Id"", ""MatchId"", ""UserId"", ""LastReadAt"", ""IsMuted"",
                       ""IsDeleted"", ""CreatedOn"", ""ModifiedOn"", ""CreatedBy"", ""ModifiedBy"")
                  VALUES ({0}, {1}, {2}, {3}, FALSE, FALSE, {3}, {3}, {4}, {4})
                  " + UpsertConflictTarget + @"
                      ""LastReadAt"" = EXCLUDED.""LastReadAt"",
                      ""ModifiedOn"" = EXCLUDED.""ModifiedOn"",
                      ""ModifiedBy"" = EXCLUDED.""ModifiedBy"",
                      ""IsDeleted""  = FALSE;",
                Guid.NewGuid(),
                matchId,
                userId,
                now,
                await ActorId(userId, userContextReader));
        }

        public async Task SetMuted(Guid matchId, Guid userId, bool muted, IUserContextReader userContextReader)
        {
            bool matchExists = await this.ContextBase.Set<MatchEntity>()
                .AnyAsync(m => m.Id == matchId);
            if (!matchExists) return;

            var now = DateTime.UtcNow;

            // On insert the cursor starts at UnixEpoch: muting must not pretend the thread has been
            // read. UnixEpoch keeps the Kind=Utc the timestamp column expects and reads as "never
            // read" downstream. On conflict only the mute flag moves - LastReadAt is left alone.
            await this.ContextBase.Database.ExecuteSqlRawAsync(
                @"INSERT INTO ""MatchChatRead""
                      (""Id"", ""MatchId"", ""UserId"", ""LastReadAt"", ""IsMuted"",
                       ""IsDeleted"", ""CreatedOn"", ""ModifiedOn"", ""CreatedBy"", ""ModifiedBy"")
                  VALUES ({0}, {1}, {2}, {3}, {4}, FALSE, {5}, {5}, {6}, {6})
                  " + UpsertConflictTarget + @"
                      ""IsMuted""    = EXCLUDED.""IsMuted"",
                      ""ModifiedOn"" = EXCLUDED.""ModifiedOn"",
                      ""ModifiedBy"" = EXCLUDED.""ModifiedBy"",
                      ""IsDeleted""  = FALSE;",
                Guid.NewGuid(),
                matchId,
                userId,
                DateTime.UnixEpoch,
                muted,
                now,
                await ActorId(userId, userContextReader));
        }

        /// <summary>
        /// CreatedBy/ModifiedBy stamp, normally set by AddEntity/UpdateEntity — the raw upserts
        /// above bypass those. Both callers only ever write the caller's own row, so the token
        /// user and <paramref name="userId"/> are the same id; the fallback just keeps the column
        /// non-null if the token is somehow unavailable.
        /// </summary>
        private static async Task<Guid> ActorId(Guid userId, IUserContextReader userContextReader)
        {
            var token = await userContextReader.GetTokenUserInfoFromContext();
            return token?.UserId ?? userId;
        }

        public async Task<List<Guid>> GetMutedMatchIds(Guid userId)
        {
            return await this.BaseDbSet()
                .Where(r => r.UserId == userId && r.IsMuted)
                .Select(r => r.MatchId)
                .ToListAsync();
        }

        public async Task<List<Guid>> GetMutedUserIds(Guid matchId)
        {
            return await this.BaseDbSet()
                .Where(r => r.MatchId == matchId && r.IsMuted)
                .Select(r => r.UserId)
                .ToListAsync();
        }
    }
}