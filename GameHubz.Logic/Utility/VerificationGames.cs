using GameHubz.DataModels.Enums;
using GameHubz.Logic.Services;

namespace GameHubz.Logic.Utility
{
    /// <summary>
    /// Which games a report needs proofs for. One definition for the report gate (BracketService) and the
    /// panel (MatchVerificationService), so the panel never promises a report the gate then refuses. A game
    /// is named by its series and its place inside it — the numbering the app shows ("Game 2",
    /// "Tiebreak · Game 1").
    /// </summary>
    public static class VerificationGames
    {
        /// <summary>Numbers each game inside its series: [S1, S1, S2] → (1, 1), (1, 2), (2, 1).</summary>
        public static List<(int Series, int Game)> Number(IEnumerable<SeriesGame> games)
        {
            var counts = new Dictionary<int, int>();
            var numbered = new List<(int Series, int Game)>();

            foreach (var game in games)
            {
                int number = counts.GetValueOrDefault(game.SeriesNumber) + 1;
                counts[game.SeriesNumber] = number;
                numbered.Add((game.SeriesNumber, number));
            }

            return numbered;
        }

        /// <summary>
        /// The games of a submission its reporter has to prove: all of them, except those already on the
        /// match's recorded result, unchanged. A tiebreak is reported together with the level series before
        /// it, and that series stands on the proofs it was reported with. A submission that leaves out a
        /// recorded game rewrites the result instead of adding to it — dropping a won tiebreak needs no new
        /// game — so nothing in it stands on old proofs: every game it reports is proven.
        /// </summary>
        public static List<(int Series, int Game)> ToProve(IReadOnlyList<SeriesGame> submitted, IReadOnlyList<SeriesGame> recorded)
        {
            var onRecord = Number(recorded)
                .Zip(recorded, (key, game) => (key, score: (game.HomeScore, game.AwayScore)))
                .ToDictionary(entry => entry.key, entry => entry.score);
            var reported = Number(submitted)
                .Zip(submitted, (key, game) => (key, score: (game.HomeScore, game.AwayScore)))
                .ToList();

            bool dropsRecordedGame = onRecord.Keys.Any(key => !reported.Any(entry => entry.key == key));

            return reported
                .Where(entry => dropsRecordedGame || !onRecord.TryGetValue(entry.key, out var recordedScore) || recordedScore != entry.score)
                .Select(entry => entry.key)
                .ToList();
        }

        /// <summary>
        /// Whether a level series of this match is settled by a tiebreak — solo knockout only; anywhere else
        /// it stands as a draw. The tiebreak can be reported in the same go as the series before it, so until
        /// the result is in, nobody can tell whether one will need proving.
        /// </summary>
        public static bool TiebreakCanFollow(MatchEntity match)
            => !match.TeamMatchId.HasValue && SeriesEvaluator.IsKnockoutStage(match.TournamentStage?.Type);

        /// <summary>
        /// Every game the match can still be reported with, as far as its state tells: each series played
        /// or proposed so far, and the tiebreak a parked match waits for, at their full Best-of. An app from
        /// before per-game verification knows one proof per match — outside a knockout it calls a player
        /// done once their proofs cover all of these, and offers its one verification until then.
        /// </summary>
        public static List<(int Series, int Game)> EveryGame(
            IReadOnlyList<SeriesGame> played,
            bool awaitingTiebreak,
            int bestOf,
            int? tiebreakBestOf)
        {
            int lastSeries = played.Count == 0 ? 1 : played.Max(g => g.SeriesNumber);
            if (awaitingTiebreak) lastSeries = Math.Min(SeriesEvaluator.MaxSeriesCount, lastSeries + 1);

            return Enumerable.Range(1, lastSeries)
                .SelectMany(series => Enumerable
                    .Range(1, SeriesEvaluator.BestOfForSeries(series, bestOf, tiebreakBestOf))
                    .Select(game => (series, game)))
                .ToList();
        }

        /// <summary>
        /// The fewest games the next report can hold, for when no score exists yet: the series still to be
        /// played (the main one, or the tiebreak a parked match waits for), up to its win target — every
        /// game of it under total score, which never settles early.
        /// </summary>
        public static List<(int Series, int Game)> ShortestNextSeries(
            IReadOnlyList<SeriesGame> recorded,
            int bestOf,
            int? tiebreakBestOf,
            TeamWinCondition condition)
        {
            int series = recorded.Count == 0 ? 1 : recorded.Max(g => g.SeriesNumber) + 1;
            int seriesBestOf = SeriesEvaluator.BestOfForSeries(series, bestOf, tiebreakBestOf);
            int games = condition == TeamWinCondition.AggregateScore ? seriesBestOf : seriesBestOf / 2 + 1;

            return Enumerable.Range(1, games).Select(game => (series, game)).ToList();
        }
    }
}
