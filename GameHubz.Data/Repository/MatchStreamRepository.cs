using GameHubz.Data.Base;
using GameHubz.Data.Context;
using GameHubz.DataModels.Domain;
using GameHubz.DataModels.Enums;
using GameHubz.Logic.Interfaces;
using GameHubz.Logic.Utility;
using Microsoft.EntityFrameworkCore;

namespace GameHubz.Data.Repository
{
    internal static class MatchStreamOrdering
    {
        // The streamer's live row first, then newest by (StartedAt, Id) — the order KeepNewestLiveRow
        // ranks sessions by. Live first because that is the row the phone is actually sending on: a
        // session that activated late (after a newer one had already ended) is still the one viewers
        // must see and Stop must close. KeepNewestLiveRow leaves at most one live row per streamer and
        // match, so "first" is unambiguous. CreatedOn only covers a row without StartedAt (none are
        // written that way); Id breaks a tie on the stored timestamp the same way the database does.
        public static IOrderedQueryable<MatchStreamEntity> CurrentFirst(this IQueryable<MatchStreamEntity> query) =>
            query.OrderByDescending(x => x.Status == MatchStreamStatus.Live)
                .ThenByDescending(x => x.StartedAt ?? x.CreatedOn)
                .ThenByDescending(x => x.Id);
    }

    public class MatchStreamRepository : BaseRepository<ApplicationContext, MatchStreamEntity>, IMatchStreamRepository
    {
        public MatchStreamRepository(
            ApplicationContext context,
            DateTimeProvider dateTimeProvider,
            IFilterExpressionBuilder filterExpressionBuilder,
            ISortStringBuilder sortStringBuilder,
            ILocalizationService localizationService)
            : base(context, dateTimeProvider, filterExpressionBuilder, sortStringBuilder, localizationService)
        {
        }

        public async Task<MatchStreamEntity?> GetLatestByMatchId(Guid matchId)
        {
            return await this.BaseDbSet()
                .Where(x => x.MatchId == matchId)
                .CurrentFirst()
                .FirstOrDefaultAsync();
        }

        public async Task<List<MatchStreamEntity>> GetByMatchId(Guid matchId)
        {
            return await this.BaseDbSet()
                .Where(x => x.MatchId == matchId)
                .CurrentFirst()
                .ToListAsync();
        }

        public async Task<MatchStreamEntity?> GetLatestByMatchAndStreamer(Guid matchId, Guid streamerUserId)
        {
            return await this.BaseDbSet()
                .Where(x => x.MatchId == matchId && x.StreamerUserId == streamerUserId)
                .CurrentFirst()
                .FirstOrDefaultAsync();
        }

        public async Task<MatchStreamEntity?> GetByBroadcastTokenHash(string tokenHash)
        {
            return await this.BaseDbSet()
                .Where(x => x.BroadcastTokenHash == tokenHash)
                .FirstOrDefaultAsync();
        }

        public async Task<bool> TouchHeartbeat(Guid streamId, DateTime now)
        {
            int updated = await this.BaseDbSet()
                .Where(x => x.Id == streamId && x.Status == MatchStreamStatus.Live)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.LastHeartbeatOn, now));
            return updated > 0;
        }

        public async Task KeepNewestLiveRow(Guid matchId, Guid streamerUserId, DateTime now)
        {
            // Compared on the stored values, in the database's own (timestamp, uuid) order, so every
            // concurrent statement ranks the rows the same way. A live row without StartedAt (none are
            // written that way) would count as older than anything.
            await this.ContextBase.Database.ExecuteSqlRawAsync(
                @"UPDATE ""MatchStream"" AS s
                  SET ""Status"" = {0}, ""EndedAt"" = COALESCE(s.""EndedAt"", {1}), ""ModifiedOn"" = {1}
                  WHERE s.""MatchId"" = {2}
                    AND s.""StreamerUserId"" = {3}
                    AND s.""Status"" = {4}
                    AND s.""IsDeleted"" = FALSE
                    AND (s.""StartedAt"" IS NULL
                         OR (s.""StartedAt"", s.""Id"") < (
                             SELECT n.""StartedAt"", n.""Id""
                             FROM ""MatchStream"" n
                             WHERE n.""MatchId"" = {2}
                               AND n.""StreamerUserId"" = {3}
                               AND n.""Status"" = {4}
                               AND n.""IsDeleted"" = FALSE
                               AND n.""StartedAt"" IS NOT NULL
                             ORDER BY n.""StartedAt"" DESC, n.""Id"" DESC
                             LIMIT 1));",
                (int)MatchStreamStatus.Ended,
                now,
                matchId,
                streamerUserId,
                (int)MatchStreamStatus.Live);
        }

        // Both inserts lean on UX_MatchStream_BroadcastTokenHash (partial unique index, migration 100):
        // ON CONFLICT DO NOTHING turns "who holds this session token" into one atomic decision.
        private const string TokenConflictTarget =
            @"ON CONFLICT (""BroadcastTokenHash"") WHERE ""BroadcastTokenHash"" IS NOT NULL DO NOTHING;";

        public async Task<bool> TryInsertLiveBroadcast(
            Guid id, Guid matchId, Guid streamerUserId, SocialType platform, string channelHandle, string tokenHash, DateTime now)
        {
            int inserted = await this.ContextBase.Database.ExecuteSqlRawAsync(
                @"INSERT INTO ""MatchStream""
                      (""Id"", ""MatchId"", ""StreamerUserId"", ""Platform"", ""ChannelHandle"", ""Status"",
                       ""StartedAt"", ""EndedAt"", ""Source"", ""BroadcastTokenHash"", ""LastHeartbeatOn"",
                       ""IsDeleted"", ""CreatedOn"", ""ModifiedOn"", ""CreatedBy"", ""ModifiedBy"")
                  VALUES ({0}, {1}, {2}, {3}, {4}, {5}, {6}, NULL, {7}, {8}, {6}, FALSE, {6}, {6}, NULL, NULL)
                  " + TokenConflictTarget,
                id,
                matchId,
                streamerUserId,
                (int)platform,
                channelHandle,
                (int)MatchStreamStatus.Live,
                now,
                (int)MatchStreamSource.AppBroadcast,
                tokenHash);
            return inserted > 0;
        }

        public async Task<bool> TryClaimStoppedBroadcast(
            Guid id, Guid matchId, Guid streamerUserId, SocialType platform, string channelHandle, string tokenHash, DateTime now)
        {
            int inserted = await this.ContextBase.Database.ExecuteSqlRawAsync(
                @"INSERT INTO ""MatchStream""
                      (""Id"", ""MatchId"", ""StreamerUserId"", ""Platform"", ""ChannelHandle"", ""Status"",
                       ""StartedAt"", ""EndedAt"", ""Source"", ""BroadcastTokenHash"", ""LastHeartbeatOn"",
                       ""IsDeleted"", ""CreatedOn"", ""ModifiedOn"", ""CreatedBy"", ""ModifiedBy"")
                  VALUES ({0}, {1}, {2}, {3}, {4}, {5}, NULL, {6}, {7}, {8}, NULL, TRUE, {6}, {6}, NULL, NULL)
                  " + TokenConflictTarget,
                id,
                matchId,
                streamerUserId,
                (int)platform,
                channelHandle,
                (int)MatchStreamStatus.Ended,
                now,
                (int)MatchStreamSource.AppBroadcast,
                tokenHash);
            return inserted > 0;
        }
    }
}
