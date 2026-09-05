using GameHubz.DataModels.Enums;
using GameHubz.Logic.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace GameHubz.Logic.Services
{
    /// <summary>
    /// Retires match evidence once it has stopped being evidence.
    ///
    /// Only video is on a clock. A screenshot costs a rounding error to keep and is the audit
    /// trail for a disputed result, so images stay; a clip is two to three orders of magnitude
    /// larger and is what actually accrues storage and bandwidth, so it goes soon after the
    /// tournament it belonged to is settled.
    ///
    /// Two rules, because one is not enough:
    ///   • the normal one, keyed off when the tournament ended;
    ///   • an age backstop, because a tournament that is abandoned rather than finished never
    ///     satisfies the first rule and would keep its clips forever.
    ///
    /// The backstop is a deliberate 30 days even though a tournament can run longer than that: a
    /// long event will lose its early clips while it is still going. That was chosen over letting
    /// an abandoned tournament sit on storage for a quarter. Both windows are configuration, so
    /// the trade can be re-made without a deploy.
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

        private int SweepBatchSize =>
            Math.Clamp(configuration.GetValue("Evidence:SweepBatchSize", 200), 1, 1000);

        /// <summary>
        /// One pass of the retention rules. Batched: a sweep that finds a large backlog chews
        /// through it a slice per tick instead of holding one enormous transaction open.
        /// </summary>
        public async Task<int> RunRetentionSweepAsync(CancellationToken cancellationToken = default)
        {
            DateTime now = DateTime.UtcNow;

            var expired = await unitOfWork.MatchEvidenceRepository.GetExpiredVideos(
                endedBefore: now.AddDays(-GraceDaysAfterTournamentEnd),
                createdBefore: now.AddDays(-MaxAgeDays),
                take: SweepBatchSize,
                cancellationToken: cancellationToken);

            if (expired.Count == 0) return 0;

            int purged = await PurgeAsync(expired, cancellationToken);

            logger.LogInformation(
                "Evidence retention sweep purged {Purged} of {Found} expired videos.",
                purged, expired.Count);

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
