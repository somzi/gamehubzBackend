using GameHubz.DataModels.Enums;

namespace GameHubz.Logic.Services
{
    /// <summary>
    /// The single source of truth for "how does a best-of series resolve". Pure functions over the
    /// stored <see cref="SeriesGame"/> list — no DB, no entity mutation — so the result path, the
    /// standings resync, the structure mapping and the tests all agree by construction. The mobile
    /// client mirrors this logic locally for instant progressive-entry feedback, but the server is
    /// authoritative: every submitted series is re-evaluated here before anything is persisted.
    /// </summary>
    public static class SeriesEvaluator
    {
        /// <summary>Best-of used by every tournament that predates the series feature.</summary>
        public const int DefaultBestOf = 1;

        /// <summary>Upper bound on a single series, so a typo can't create a 500-game match.</summary>
        public const int MaxBestOf = 15;

        /// <summary>Upper bound on tiebreak series, so an endless draw chain can't grow unbounded.</summary>
        public const int MaxSeriesCount = 10;

        /// <summary>
        /// The result of reading a whole match's games: the headline score that lands on
        /// <c>Match.HomeUserScore</c>/<c>AwayUserScore</c>, the real goal totals that feed
        /// standings, and whether the match is decided, level, or still mid-series.
        /// </summary>
        public sealed class SeriesOutcome
        {
            /// <summary>Score shown on the card — the deciding (last) series, since that is what settles the match.</summary>
            public int HomeHeadline { get; init; }

            public int AwayHeadline { get; init; }

            /// <summary>Real goals across every game of every series. Drives GF/GA, never the winner.</summary>
            public int HomeGoals { get; init; }

            public int AwayGoals { get; init; }

            /// <summary>Number of the series currently in play (1 = main series).</summary>
            public int CurrentSeriesNumber { get; init; }

            /// <summary>Best-of that applies to <see cref="CurrentSeriesNumber"/>.</summary>
            public int CurrentSeriesBestOf { get; init; }

            /// <summary>Games already recorded in the current series.</summary>
            public int GamesInCurrentSeries { get; init; }

            /// <summary>True once the current series can take no further games (clinched or full).</summary>
            public bool CurrentSeriesOver { get; init; }

            /// <summary>True when the current series is over and level — a draw, or a pending tiebreak.</summary>
            public bool IsLevel { get; init; }

            /// <summary>True when a further game may be added to the current series.</summary>
            public bool CanAddGame => !CurrentSeriesOver;

            /// <summary>Home won the deciding series.</summary>
            public bool HomeWon => CurrentSeriesOver && HomeHeadline > AwayHeadline;

            /// <summary>Away won the deciding series.</summary>
            public bool AwayWon => CurrentSeriesOver && AwayHeadline > HomeHeadline;
        }

        /// <summary>
        /// Effective Best-of for a given series number: the main series uses the match format,
        /// every tiebreak series uses the tiebreak format (falling back to the match format when
        /// the organizer never set a distinct one — "a drawn Bo3 replays as another Bo3").
        /// </summary>
        public static int BestOfForSeries(int seriesNumber, int matchBestOf, int? tiebreakBestOf)
            => seriesNumber <= 1
                ? Normalize(matchBestOf)
                : Normalize(tiebreakBestOf ?? matchBestOf);

        /// <summary>Clamps an organizer-supplied Best-of into the supported range.</summary>
        public static int Normalize(int? bestOf)
        {
            var value = bestOf ?? DefaultBestOf;
            if (value < 1) return DefaultBestOf;
            return value > MaxBestOf ? MaxBestOf : value;
        }

        /// <summary>
        /// Reads a match's games into a <see cref="SeriesOutcome"/>. An empty list describes a match
        /// that has not started: series 1, nothing played, nothing decided.
        /// </summary>
        public static SeriesOutcome Evaluate(
            IReadOnlyList<SeriesGame>? games,
            TeamWinCondition condition,
            int matchBestOf,
            int? tiebreakBestOf)
        {
            var all = games ?? Array.Empty<SeriesGame>();

            int homeGoals = 0, awayGoals = 0;
            foreach (var g in all)
            {
                homeGoals += g.HomeScore;
                awayGoals += g.AwayScore;
            }

            var currentSeriesNumber = all.Count == 0 ? 1 : all.Max(g => g.SeriesNumber);
            var currentGames = all.Where(g => g.SeriesNumber == currentSeriesNumber).ToList();
            var bestOf = BestOfForSeries(currentSeriesNumber, matchBestOf, tiebreakBestOf);

            var (home, away, over) = ScoreSeries(currentGames, condition, bestOf);

            return new SeriesOutcome
            {
                HomeHeadline = home,
                AwayHeadline = away,
                HomeGoals = homeGoals,
                AwayGoals = awayGoals,
                CurrentSeriesNumber = currentSeriesNumber,
                CurrentSeriesBestOf = bestOf,
                GamesInCurrentSeries = currentGames.Count,
                CurrentSeriesOver = over,
                IsLevel = over && home == away,
            };
        }

        /// <summary>
        /// Scores one series and decides whether it can still take games.
        /// </summary>
        /// <remarks>
        /// A single-game series always reports the raw game score, whichever criterion is set —
        /// otherwise every Bo1 match would display "1–0" instead of the score that was played.
        /// <para/>
        /// Under <see cref="TeamWinCondition.MatchWins"/> the series ends early once one side's win
        /// count cannot be caught: <c>wins &gt; opponentWins + gamesRemaining</c>. That general form is
        /// what makes drawn games safe — the usual "first to ⌈N/2⌉" shortcut silently assumes every
        /// game has a winner, so a Bo3 sitting at 1–0 with a drawn game would wrongly look unfinished.
        /// <para/>
        /// Under <see cref="TeamWinCondition.AggregateScore"/> there is no early clinch: game scores
        /// are unbounded, so any remaining game could still overturn any lead. The series runs its
        /// full Best-of.
        /// </remarks>
        private static (int Home, int Away, bool Over) ScoreSeries(
            List<SeriesGame> games,
            TeamWinCondition condition,
            int bestOf)
        {
            var played = games.Count;
            var remaining = Math.Max(0, bestOf - played);

            // Bo1 is its own game: report what was played, not a 1–0 win tally.
            if (bestOf <= 1 || condition == TeamWinCondition.AggregateScore)
            {
                int home = 0, away = 0;
                foreach (var g in games)
                {
                    home += g.HomeScore;
                    away += g.AwayScore;
                }

                return (home, away, remaining == 0);
            }

            int homeWins = 0, awayWins = 0;
            foreach (var g in games)
            {
                if (g.HomeScore > g.AwayScore) homeWins++;
                else if (g.AwayScore > g.HomeScore) awayWins++;
                // a drawn game counts for neither side
            }

            var clinched = homeWins > awayWins + remaining || awayWins > homeWins + remaining;

            return (homeWins, awayWins, clinched || remaining == 0);
        }

        /// <summary>
        /// Validates a submitted game list before anything is persisted. Throws a descriptive
        /// message for the client; returns the evaluated outcome when the submission is sound.
        /// </summary>
        /// <remarks>
        /// Rules enforced: at least one game; series numbered 1..N with no gaps; no series longer
        /// than its own Best-of; every series except the last finished level (a tiebreak only
        /// exists because the series before it drew); the last series played out to a decision or
        /// a level finish; and non-negative scores.
        /// </remarks>
        public static SeriesOutcome ValidateAndEvaluate(
            IReadOnlyList<SeriesGame>? games,
            TeamWinCondition condition,
            int matchBestOf,
            int? tiebreakBestOf)
        {
            if (games == null || games.Count == 0)
                throw new BusinessRuleException("Report at least one game for this match.");

            if (games.Any(g => g.HomeScore < 0 || g.AwayScore < 0))
                throw new BusinessRuleException("Game scores cannot be negative.");

            var seriesNumbers = games.Select(g => g.SeriesNumber).Distinct().OrderBy(n => n).ToList();

            if (seriesNumbers[0] != 1)
                throw new BusinessRuleException("The first series must be numbered 1.");

            if (seriesNumbers.Count > MaxSeriesCount)
                throw new BusinessRuleException($"A match cannot go beyond {MaxSeriesCount - 1} tiebreaks.");

            for (int i = 0; i < seriesNumbers.Count; i++)
            {
                if (seriesNumbers[i] != i + 1)
                    throw new BusinessRuleException("Tiebreak series must be consecutive — a series is missing.");
            }

            // Every series but the last must be complete AND level: a tiebreak exists only because
            // the series before it finished all square.
            for (int i = 0; i < seriesNumbers.Count - 1; i++)
            {
                var number = seriesNumbers[i];
                var bestOf = BestOfForSeries(number, matchBestOf, tiebreakBestOf);
                var slice = games.Where(g => g.SeriesNumber == number).ToList();

                if (slice.Count > bestOf)
                    throw new BusinessRuleException($"Series {number} has more games than its best-of-{bestOf} format allows.");

                var (home, away, over) = ScoreSeries(slice, condition, bestOf);
                if (!over)
                    throw new BusinessRuleException($"Series {number} is not finished, so a tiebreak cannot start.");
                if (home != away)
                    throw new BusinessRuleException($"Series {number} already has a winner, so a tiebreak cannot start.");
            }

            var lastNumber = seriesNumbers[^1];
            var lastBestOf = BestOfForSeries(lastNumber, matchBestOf, tiebreakBestOf);
            var lastSlice = games.Where(g => g.SeriesNumber == lastNumber).ToList();

            if (lastSlice.Count > lastBestOf)
                throw new BusinessRuleException($"This match is a best-of-{lastBestOf} — you reported {lastSlice.Count} games.");

            var outcome = Evaluate(games, condition, matchBestOf, tiebreakBestOf);

            if (!outcome.CurrentSeriesOver)
                throw new BusinessRuleException("This series is not finished yet — report the remaining games.");

            return outcome;
        }
    }
}
