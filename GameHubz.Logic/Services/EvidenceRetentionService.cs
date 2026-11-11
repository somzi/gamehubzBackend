using GameHubz.DataModels.Enums;
using GameHubz.Logic.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace GameHubz.Logic.Services
{
    /// <summary>
    /// Retires match evidence once it has stopped being evidence.
    ///
    /// Clips and screenshots are on separate clocks, because they cost differently. A clip is two
    /// to three orders of magnitude larger and is what actually accrues storage and bandwidth, so
    /// it goes as soon as the result it proves has stood for a while. A screenshot is small, so it
    /// is kept for the whole tournament — but not forever: storage is billed on everything ever
    /// uploaded, and a screenshot of a result nobody can dispute any more is only that bill.
    ///
    /// Clips, three rules:
    ///   • the normal one, keyed off the clip's own match: settled and left alone for the window,
    ///     with no open admin-help request. A league runs for weeks, and keyed off the tournament
    ///     alone every clip of it sat in storage until the final;
    ///   • the tournament ended;
    ///   • an age backstop, because a match that is never settled in a tournament that is
    ///     abandoned rather than finished satisfies neither rule and would keep its clips forever.
    ///
    /// Screenshots, two rules: the tournament ended, or the same kind of age backstop.
    ///
    /// The backstops are a deliberate 30 days even though a tournament can run longer than that: a
    /// long event will lose its early evidence while it is still going. That was chosen over
    /// letting an abandoned tournament sit on storage for a quarter. Every window is configuration,
    /// so the trade can be re-made without a deploy.
    ///
    /// Retiring is a soft delete on purpose: the row that stays behind is how a reader tells
    /// "expired" from "never attached" (see IMatchEvidenceRepository.GetMatchIdsWithExpiredEvidence).
    /// </summary>
    public class EvidenceRetentionService
    {
        private readonly IAppUnitOfWork unitOfWork;
        private readonly IStorageService storageService;
        private readonly IUserContextReader userContextReader;
        private readonly ILogger<EvidenceRetentionService> logger;
        private readonly IConfiguration configuration;

        public EvidenceRetentionService(
            IUnitOfWorkFactory factory,
            IStorageService storageService,
            IUserContextReader userContextReader,
            ILogger<EvidenceRetentionService> logger,
            IConfiguration configuration)
        {
            this.unitOfWork = factory.CreateAppUnitOfWork();
            this.storageService = storageService;
            this.userContextReader = userContextReader;
            this.logger = logger;
            this.configuration = configuration;
        }

        private int GraceDaysAfterTournamentEnd =>
            Math.Max(0, configuration.GetValue("Evidence:VideoRetentionDaysAfterTournamentEnd", 3));

        private int MaxAgeDays =>
            Math.Max(1, configuration.GetValue("Evidence:VideoMaxAgeDays", 30));

        /// <summary>
        /// How long a clip outlives its own match being settled. Zero or less switches the
        /// per-match rule off and leaves clips to the tournament-end rule and the age backstop.
        /// </summary>
        private int VideoDaysAfterMatchSettled =>
            configuration.GetValue("Evidence:VideoRetentionDaysAfterMatchEnd", 7);

        /// <summary>Master switch for the screenshot clock; off keeps every screenshot forever.</summary>
        private bool ImageRetentionEnabled =>
            configuration.GetValue("Evidence:ImageRetentionEnabled", true);

        private int ImageGraceDaysAfterTournamentEnd =>
            Math.Max(0, configuration.GetValue("Evidence:ImageRetentionDaysAfterTournamentEnd", 7));

        private int ImageMaxAgeDays =>
            Math.Max(1, configuration.GetValue("Evidence:ImageMaxAgeDays", 30));

        private int SweepBatchSize =>
            Math.Clamp(configuration.GetValue("Evidence:SweepBatchSize", 200), 1, 1000);

        /// <summary>
        /// One pass of the retention rules. Batched: a sweep that finds a large backlog chews
        /// through it a slice per tick instead of holding one enormous transaction open. Clips and
        /// screenshots each get their own slice, so a backlog of one never starves the other.
        /// </summary>
        public async Task<int> RunRetentionSweepAsync(CancellationToken cancellationToken = default)
        {
            DateTime now = DateTime.UtcNow;
            int perMatchDays = VideoDaysAfterMatchSettled;
            int purged = 0;

            var expiredVideos = await unitOfWork.MatchEvidenceRepository.GetExpiredVideos(
                // Older than every row there is: the rule can never match, which is "off".
                matchSettledBefore: perMatchDays > 0 ? now.AddDays(-perMatchDays) : DateTime.UnixEpoch,
                endedBefore: now.AddDays(-GraceDaysAfterTournamentEnd),
                createdBefore: now.AddDays(-MaxAgeDays),
                take: SweepBatchSize,
                cancellationToken: cancellationToken);

            if (expiredVideos.Count > 0)
            {
                int purgedVideos = await PurgeAsync(expiredVideos, cancellationToken);
                purged += purgedVideos;

                logger.LogInformation(
                    "Evidence retention sweep purged {Purged} of {Found} expired videos.",
                    purgedVideos, expiredVideos.Count);
            }

            if (!ImageRetentionEnabled || cancellationToken.IsCancellationRequested) return purged;

            var expiredImages = await unitOfWork.MatchEvidenceRepository.GetExpiredImages(
                endedBefore: now.AddDays(-ImageGraceDaysAfterTournamentEnd),
                createdBefore: now.AddDays(-ImageMaxAgeDays),
                take: SweepBatchSize,
                cancellationToken: cancellationToken);

            if (expiredImages.Count > 0)
            {
                int purgedImages = await PurgeAsync(expiredImages, cancellationToken);
                purged += purgedImages;

                logger.LogInformation(
                    "Evidence retention sweep purged {Purged} of {Found} expired screenshots.",
                    purgedImages, expiredImages.Count);
            }

            return purged;
        }

        /// <summary>
        /// Drops a tournament's clips immediately. Called when it is cancelled or deleted: there
        /// is no result left to dispute, so the grace window has nothing to protect.
        ///
        /// Never throws. It runs as a side effect of cancelling a tournament, and a storage
        /// hiccup must not fail that — anything left behind is collected by the periodic sweep.
        /// The failure is logged here rather than swallowed at the call site, which has no logger.
        /// </summary>
        public async Task<int> PurgeTournamentVideosAsync(Guid tournamentId, CancellationToken cancellationToken = default)
        {
            try
            {
                var rows = await unitOfWork.MatchEvidenceRepository
                    .GetByTournament(tournamentId, EvidenceMediaType.Video);

                if (rows.Count == 0) return 0;

                int purged = await PurgeAsync(rows, cancellationToken);

                logger.LogInformation(
                    "Purged {Purged} of {Found} videos for retired tournament {TournamentId}.",
                    purged, rows.Count, tournamentId);

                return purged;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Immediate video purge failed for tournament {TournamentId}.", tournamentId);
                return 0;
            }
        }

        /// <summary>
        /// Deletes the assets, then retires the rows that actually went. A row whose asset could
        /// not be removed is deliberately left live so the next sweep retries it — marking it
        /// deleted would lose the only handle we have and strand the file in storage forever.
        /// </summary>
        private async Task<int> PurgeAsync(
            IReadOnlyCollection<MatchEvidenceEntity> rows,
            CancellationToken cancellationToken)
        {
            int purged = 0;

            foreach (var row in rows)
            {
                if (cancellationToken.IsCancellationRequested) break;

                try
                {
                    if (string.IsNullOrWhiteSpace(row.StorageKey))
                    {
                        // Pre-migration rows never recorded a handle, so their file cannot be
                        // addressed. Retire the row anyway: leaving it live would park it at the
                        // head of the oldest-first sweep and block everything behind it forever.
                        logger.LogWarning(
                            "Evidence {EvidenceId} has no storage key; retiring the row and leaving the asset in place.",
                            row.Id);
                    }
                    else
                    {
                        await storageService.DeleteAsync(row.StorageKey, row.MediaType, cancellationToken);
                    }

                    await unitOfWork.MatchEvidenceRepository.SoftDeleteEntity(row, userContextReader);
                    purged++;
                }
                catch (Exception ex)
                {
                    // One bad asset must not abort the batch; the row stays live and comes back
                    // around on the next tick.
                    logger.LogError(ex, "Failed to purge evidence {EvidenceId}.", row.Id);
                }
            }

            if (purged > 0)
            {
                await unitOfWork.SaveChangesAsync();
            }

            return purged;
        }
    }
}
