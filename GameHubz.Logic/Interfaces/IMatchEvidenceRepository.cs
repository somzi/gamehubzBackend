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
        /// Three independent reasons a clip qualifies, and they are ORed on purpose:
        ///   • its match was settled and has been left alone since before
        ///     <paramref name="matchSettledBefore"/> — the normal path. A clip proves one result;
        ///     once that result has stood untouched for the window, nobody is going to watch it.
        ///     A match with an open admin-help request or a pending proposal never qualifies here,
        ///     and neither does a clip younger than the window (one attached late is being attached
        ///     because someone is disputing the result). A cut-off older than every row
        ///     (<see cref="DateTime.UnixEpoch"/>) switches this rule off;
        ///   • its tournament ended before <paramref name="endedBefore"/>;
        ///   • it is simply older than <paramref name="createdBefore"/> — the backstop for a
        ///     match that is never settled in a tournament that never formally ends, which would
        ///     otherwise keep its evidence forever because neither rule above ever fires.
        ///
        /// The backstop window is shorter than the longest tournaments run, so an event lasting
        /// past it loses its early clips while still live. Deliberate — see EvidenceRetentionService.
        /// </summary>
        Task<List<MatchEvidenceEntity>> GetExpiredVideos(
            DateTime matchSettledBefore,
            DateTime endedBefore,
            DateTime createdBefore,
            int take,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Screenshots that have outlived their purpose, oldest first: the tournament ended before
        /// <paramref name="endedBefore"/> (and the screenshot itself is older than that too), or the
        /// screenshot is older than <paramref name="createdBefore"/> in a tournament still running.
        /// </summary>
        Task<List<MatchEvidenceEntity>> GetExpiredImages(
            DateTime endedBefore,
            DateTime createdBefore,
            int take,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// The matches of a tournament that once carried evidence the retention sweep has since
        /// retired. Lets a reader tell "nothing was ever attached" from "it was, and it expired".
        /// </summary>
        Task<HashSet<Guid>> GetMatchIdsWithExpiredEvidenceByTournament(Guid tournamentId);

        /// <summary>Same question as above, asked about specific matches.</summary>
        Task<HashSet<Guid>> GetMatchIdsWithExpiredEvidence(IReadOnlyCollection<Guid> matchIds);

        /// <summary>
        /// Every live evidence row of the given media type on a tournament. Used to purge at once
        /// when a tournament is cancelled or deleted, where there is nothing left to prove.
        /// </summary>
        Task<List<MatchEvidenceEntity>> GetByTournament(Guid tournamentId, EvidenceMediaType mediaType);

        /// <summary>
        /// Whether any of these matches carries evidence. Pass <paramref name="includeSoftDeleted"/> before
        /// hard-deleting the matches: a soft-deleted row still holds the foreign key.
        /// </summary>
        Task<bool> AnyForMatches(IReadOnlyCollection<Guid> matchIds, bool includeSoftDeleted);
    }
}
