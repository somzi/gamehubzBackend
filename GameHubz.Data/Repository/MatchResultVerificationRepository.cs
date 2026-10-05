using GameHubz.Data.Base;
using GameHubz.Data.Context;
using GameHubz.DataModels.Domain;
using GameHubz.DataModels.Enums;
using GameHubz.DataModels.Models;
using GameHubz.Logic.Interfaces;
using GameHubz.Logic.Utility;
using Microsoft.EntityFrameworkCore;

namespace GameHubz.Data.Repository
{
    public class MatchResultVerificationRepository : BaseRepository<ApplicationContext, MatchResultVerificationEntity>, IMatchResultVerificationRepository
    {
        public MatchResultVerificationRepository(
            ApplicationContext context,
            DateTimeProvider dateTimeProvider,
            IFilterExpressionBuilder filterExpressionBuilder,
            ISortStringBuilder sortStringBuilder,
            ILocalizationService localizationService)
            : base(context, dateTimeProvider, filterExpressionBuilder, sortStringBuilder, localizationService)
        {
        }

        public Task<bool> HasVerified(Guid matchId, Guid userId, int seriesNumber, int gameNumber)
        {
            // A whole-match proof covers every game; asked about the whole match itself, only one answers.
            return this.BaseDbSet()
                .AnyAsync(v => v.MatchId == matchId
                    && v.UserId == userId
                    && v.Status == MatchVerificationStatus.Verified
                    && (v.GameNumber == MatchResultVerificationEntity.WholeMatch
                        || (v.SeriesNumber == seriesNumber && v.GameNumber == gameNumber)));
        }

        public async Task<bool> HasVerifiedGames(Guid matchId, Guid userId, IReadOnlyCollection<(int Series, int Game)> games)
        {
            if (games.Count == 0) return true;

            var verified = await this.BaseDbSet()
                .Where(v => v.MatchId == matchId && v.UserId == userId && v.Status == MatchVerificationStatus.Verified)
                .Select(v => new { v.SeriesNumber, v.GameNumber })
                .Distinct()
                .ToListAsync();

            return verified.Any(v => v.GameNumber == MatchResultVerificationEntity.WholeMatch)
                || games.All(game => verified.Any(v => v.SeriesNumber == game.Series && v.GameNumber == game.Game));
        }

        public Task<bool> IsRecordingUsedForAnotherGame(
            Guid matchId,
            Guid userId,
            Guid verificationId,
            DateTime? recordedOn,
            int? durationMs)
        {
            // The same clip was recorded at the same moment and runs exactly as long. Never the name —
            // phones and apps reuse generic ones, and two games can last the same — and without both facts
            // there is nothing safe to refuse on: a guess must not stop an honest player.
            if (recordedOn == null || durationMs == null) return Task.FromResult(false);

            return this.BaseDbSet()
                .AnyAsync(v => v.MatchId == matchId
                    && v.UserId == userId
                    && v.Id != verificationId
                    && v.Status == MatchVerificationStatus.Verified
                    && (v.RawRecordedOn ?? v.RecordedOn) == recordedOn
                    && v.EvidenceDurationMs == durationMs);
        }

        public Task<bool> AnyVerifiedForMatches(IReadOnlyCollection<Guid> matchIds)
        {
            if (matchIds.Count == 0) return Task.FromResult(false);

            return this.BaseDbSet()
                .AnyAsync(v => matchIds.Contains(v.MatchId) && v.Status == MatchVerificationStatus.Verified);
        }

        public Task<MatchResultVerificationEntity?> GetForUpdate(Guid verificationId)
        {
            return this.BaseDbSet()
                .FirstOrDefaultAsync(v => v.Id == verificationId);
        }

        public Task<List<MatchResultVerificationEntity>> GetShownForMatch(Guid matchId)
        {
            // Started / BiometricVerified rows are attempts in flight or abandoned — a cancelled Face ID
            // prompt says nothing about the match — so only the two terminal outcomes are read.
            return this.BaseDbSet()
                .Include(v => v.User)
                .Include(v => v.UserDevice)
                .Include(v => v.MatchEvidence)
                .Where(v => v.MatchId == matchId
                    && (v.Status == MatchVerificationStatus.Verified || v.Status == MatchVerificationStatus.Failed))
                .OrderByDescending(v => v.CreatedOn)
                .ToListAsync();
        }

        public Task<int> CountStartedSince(Guid matchId, Guid userId, DateTime since)
        {
            return this.BaseDbSet()
                .CountAsync(v => v.MatchId == matchId && v.UserId == userId && v.CreatedOn >= since);
        }

        public async Task<bool> TryClaimEvidenceUpload(Guid verificationId, DateTime claimedOn, DateTime staleBefore)
        {
            // One conditional UPDATE: whichever request reaches the row first takes the claim, and the
            // other updates nothing. ExecuteUpdate goes straight to the database, past the change
            // tracker, which is exactly what makes it a claim rather than a read followed by a write.
            int affected = await this.BaseDbSet()
                .Where(v => v.Id == verificationId
                    && v.Status == MatchVerificationStatus.BiometricVerified
                    && (v.EvidenceUploadClaimedOn == null || v.EvidenceUploadClaimedOn < staleBefore))
                .ExecuteUpdateAsync(s => s.SetProperty(v => v.EvidenceUploadClaimedOn, claimedOn));

            return affected > 0;
        }

        public async Task<bool> TryCompleteEvidenceUpload(Guid verificationId, DateTime claimedOn, VerificationUploadResult result)
        {
            // Conditional on the claim this request took: a request overtaken while it was uploading
            // updates nothing here, rather than overwriting the attempt its takeover already completed.
            int affected = await this.BaseDbSet()
                .Where(v => v.Id == verificationId
                    && v.Status == MatchVerificationStatus.BiometricVerified
                    && v.EvidenceUploadClaimedOn == claimedOn)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(v => v.Status, MatchVerificationStatus.Verified)
                    .SetProperty(v => v.MatchEvidenceId, (Guid?)result.EvidenceId)
                    .SetProperty(v => v.EvidenceUploadedOn, (DateTime?)result.CompletedOn)
                    .SetProperty(v => v.VerifiedOn, (DateTime?)result.CompletedOn)
                    .SetProperty(v => v.RecordedOn, result.RecordedOn)
                    .SetProperty(v => v.RawRecordedOn, result.RawRecordedOn)
                    .SetProperty(v => v.EvidenceDurationMs, result.DurationMs)
                    .SetProperty(v => v.EvidenceFileName, result.FileName)
                    .SetProperty(v => v.ModifiedOn, (DateTime?)result.CompletedOn)
                    .SetProperty(v => v.ModifiedBy, (Guid?)result.CompletedBy));

            return affected > 0;
        }

        public Task ReleaseEvidenceUploadClaim(Guid verificationId, DateTime claimedOn)
        {
            // Only our own claim, and only while the attempt is still waiting: a request that reclaimed
            // it after ours went stale — or one that already completed it — is left alone.
            return this.BaseDbSet()
                .Where(v => v.Id == verificationId
                    && v.Status == MatchVerificationStatus.BiometricVerified
                    && v.EvidenceUploadClaimedOn == claimedOn)
                .ExecuteUpdateAsync(s => s.SetProperty(v => v.EvidenceUploadClaimedOn, (DateTime?)null));
        }
    }
}
