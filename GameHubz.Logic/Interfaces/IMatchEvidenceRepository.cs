using GameHubz.DataModels.Enums;

namespace GameHubz.Logic.Interfaces
{
    public interface IMatchEvidenceRepository : IRepository<MatchEvidenceEntity>
    {
        /// <summary>How many clips a match already carries, for the per-match video cap.</summary>
        Task<int> CountVideosForMatch(Guid matchId);

        /// <summary>
        /// Video evidence that has outlived its purpose, oldest first.
        ///
        /// Two independent reasons a clip qualifies, and they are ORed on purpose:
        ///   • its tournament ended before <paramref name="endedBefore"/> — the normal path;
        ///   • it is simply older than <paramref name="createdBefore"/> — the backstop for a
        ///     tournament that never formally ends (abandoned, organizer gone), which would
        ///     otherwise keep its evidence forever because the first condition never fires.
        ///
        /// The backstop window is shorter than the longest tournaments run, so an event lasting
        /// past it loses its early clips while still live. Deliberate — see EvidenceRetentionService.
        /// </summary>
        Task<List<MatchEvidenceEntity>> GetExpiredVideos(
            DateTime endedBefore,
            DateTime createdBefore,
            int take,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Every live evidence row of the given media type on a tournament. Used to purge at once
        /// when a tournament is cancelled or deleted, where there is nothing left to prove.
        /// </summary>
        Task<List<MatchEvidenceEntity>> GetByTournament(Guid tournamentId, EvidenceMediaType mediaType);
    }
}
