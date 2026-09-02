using System.Collections.Generic;

using NUnit.Framework;

using GameHubz.DataModels.Enums;
using GameHubz.DataModels.Models;
using GameHubz.Logic.Exceptions;
using GameHubz.Logic.Interfaces;
using GameHubz.Logic.Services;
using GameHubz.Logic.Test.Factories;

namespace GameHubz.Logic.Test.Bracket
{
    // The evaluator writes its rejections through the localization service, so the tests use a
    // real one rather than a stub: the message a player would actually read is what gets built.
    // Pure unit tests over the series maths — no DB, no harness. These pin the rules the whole
    // best-of feature rests on: when a series is decided, what its headline score is, and which
    // submissions the server refuses. The mobile client mirrors this logic for its entry form, so
    // a change here is a change there.
    [TestFixture]
    internal sealed class SeriesEvaluatorTests
    {
        private static readonly ILocalizationService Localization =
            new LocalizationServiceFactory().CreateService();

        private static SeriesGame G(int home, int away, int series = 1)
            => new() { HomeScore = home, AwayScore = away, SeriesNumber = series };

        private static SeriesEvaluator.SeriesOutcome Eval(
            List<SeriesGame> games,
            TeamWinCondition condition = TeamWinCondition.MatchWins,
            int bestOf = 3,
            int? tiebreakBestOf = null)
            => SeriesEvaluator.Evaluate(games, condition, bestOf, tiebreakBestOf);

        // ── Bo1 ────────────────────────────────────────────────────────────────

        [Test]
        public void Bo1_ReportsRawScore_NotAWinTally()
        {
            // The whole point: a single game must display what was played (3–2), never "1–0".
            var outcome = Eval([G(3, 2)], TeamWinCondition.MatchWins, bestOf: 1);

            Assert.Multiple(() =>
            {
                Assert.That(outcome.HomeHeadline, Is.EqualTo(3));
                Assert.That(outcome.AwayHeadline, Is.EqualTo(2));
                Assert.That(outcome.CurrentSeriesOver, Is.True);
                Assert.That(outcome.IsLevel, Is.False);
            });
        }

        [Test]
        public void Bo1_LevelGame_IsLevel()
        {
            var outcome = Eval([G(2, 2)], TeamWinCondition.MatchWins, bestOf: 1);

            Assert.That(outcome.IsLevel, Is.True);
            Assert.That(outcome.CurrentSeriesOver, Is.True);
        }

        // ── MatchWins clinching ────────────────────────────────────────────────

        [Test]
        public void MatchWins_Bo3_ClinchesAfterTwoStraight()
        {
            var outcome = Eval([G(1, 0), G(2, 1)], bestOf: 3);

            Assert.Multiple(() =>
            {
                Assert.That(outcome.HomeHeadline, Is.EqualTo(2), "headline is games won");
                Assert.That(outcome.AwayHeadline, Is.EqualTo(0));
                Assert.That(outcome.CurrentSeriesOver, Is.True, "2–0 in a Bo3 cannot be caught");
                Assert.That(outcome.CanAddGame, Is.False);
                Assert.That(outcome.HomeGoals, Is.EqualTo(3), "goals are the real scores, not the tally");
                Assert.That(outcome.AwayGoals, Is.EqualTo(1));
            });
        }

        [Test]
        public void MatchWins_Bo7_WonFourNil_StopsAskingForGames()
        {
            // The "don't show 7 blank inputs" rule, stated as maths.
            var outcome = Eval([G(1, 0), G(1, 0), G(1, 0), G(1, 0)], bestOf: 7);

            Assert.That(outcome.CurrentSeriesOver, Is.True);
            Assert.That(outcome.GamesInCurrentSeries, Is.EqualTo(4));
        }

        [Test]
        public void MatchWins_Bo3_OneWinAndTwoDraws_DecidesOnWinsAlone()
        {
            // Drawn games are allowed, so the usual "first to 2" shortcut never fires here — the
            // series still has a winner because one side simply won more games.
            var outcome = Eval([G(1, 0), G(1, 1), G(2, 2)], bestOf: 3);

            Assert.Multiple(() =>
            {
                Assert.That(outcome.HomeHeadline, Is.EqualTo(1));
                Assert.That(outcome.AwayHeadline, Is.EqualTo(0));
                Assert.That(outcome.CurrentSeriesOver, Is.True);
                Assert.That(outcome.IsLevel, Is.False);
            });
        }

        [Test]
        public void MatchWins_OddBestOf_CanStillEndLevel_BecauseGamesMayBeDrawn()
        {
            // Explicitly pinned: an odd Best-of is NOT draw-proof once drawn games exist. Every
            // knockout format therefore needs the tiebreak path, not just the even ones.
            var outcome = Eval([G(1, 1), G(2, 2), G(0, 0)], bestOf: 3);

            Assert.That(outcome.IsLevel, Is.True);
            Assert.That(outcome.CurrentSeriesOver, Is.True);
        }

        [Test]
        public void MatchWins_Bo3_OneAllAfterTwo_StillOpen()
        {
            var outcome = Eval([G(1, 0), G(0, 1)], bestOf: 3);

            Assert.That(outcome.CurrentSeriesOver, Is.False);
            Assert.That(outcome.CanAddGame, Is.True);
        }

        // ── AggregateScore ─────────────────────────────────────────────────────

        [Test]
        public void Aggregate_SumsGoals_AndRunsTheFullBestOf()
        {
            // No early clinch is possible: game scores are unbounded, so any remaining game could
            // overturn any lead. A 9–0 first leg still plays the second.
            var partial = Eval([G(9, 0)], TeamWinCondition.AggregateScore, bestOf: 2);
            Assert.That(partial.CurrentSeriesOver, Is.False, "an unbounded score can always be caught up");

            var full = Eval([G(4, 1), G(1, 3)], TeamWinCondition.AggregateScore, bestOf: 2);
            Assert.Multiple(() =>
            {
                Assert.That(full.HomeHeadline, Is.EqualTo(5));
                Assert.That(full.AwayHeadline, Is.EqualTo(4));
                Assert.That(full.CurrentSeriesOver, Is.True);
            });
        }

        [Test]
        public void Aggregate_EqualTotals_IsLevel()
        {
            var outcome = Eval([G(2, 1), G(1, 2)], TeamWinCondition.AggregateScore, bestOf: 2);

            Assert.That(outcome.IsLevel, Is.True);
        }

        // ── Tiebreak series ────────────────────────────────────────────────────

        [Test]
        public void Tiebreak_HeadlineCountsEverySeries_SoItMatchesTheGamesListed()
        {
            // Main series drew 1–1 by wins; the tiebreak replay decides it 2–0. The headline is the
            // whole encounter — 3–1 — because that is what someone reading the five games adds up.
            // Reporting the tiebreak alone would show "2–0", a figure matching nothing on screen.
            var games = new List<SeriesGame>
            {
                G(1, 0), G(0, 1), G(2, 2),              // main series: 1–1 on wins
                G(3, 1, series: 2), G(2, 0, series: 2), // tiebreak: 2–0
            };

            var outcome = Eval(games, bestOf: 3);

            Assert.Multiple(() =>
            {
                Assert.That(outcome.CurrentSeriesNumber, Is.EqualTo(2));
                Assert.That(outcome.HomeHeadline, Is.EqualTo(3), "1 win in the main series plus 2 in the tiebreak");
                Assert.That(outcome.AwayHeadline, Is.EqualTo(1));
                Assert.That(outcome.IsLevel, Is.False);
                Assert.That(outcome.HomeGoals, Is.EqualTo(8), "1+0+2+3+2 across every series");
                Assert.That(outcome.AwayGoals, Is.EqualTo(4), "0+1+2+1+0");
            });
        }

        [Test]
        public void Tiebreak_SummingSeriesKeepsTheWinnerTheDecidingSeriesPicked()
        {
            // The property that makes the sum safe: every series before the last is level, so it
            // adds the same amount to both sides. Winner and margin survive untouched.
            var games = new List<SeriesGame>
            {
                G(1, 0), G(0, 1),                       // main series: 1–1 on wins
                G(0, 1, series: 2), G(0, 2, series: 2), // tiebreak: away takes it 2–0
            };

            var outcome = Eval(games, TeamWinCondition.MatchWins, bestOf: 2);

            Assert.Multiple(() =>
            {
                Assert.That(outcome.AwayWon, Is.True);
                Assert.That(outcome.AwayHeadline - outcome.HomeHeadline, Is.EqualTo(2), "margin is the deciding series' margin");
            });
        }

        [Test]
        public void Tiebreak_UsesTiebreakBestOf_WhenTheOrganizerSetOne()
        {
            var games = new List<SeriesGame> { G(1, 1), G(5, 4, series: 2) };

            // Match is a Bo3, tiebreak is an explicit Bo1 — so one game settles the replay.
            var outcome = Eval(games, TeamWinCondition.MatchWins, bestOf: 3, tiebreakBestOf: 1);

            Assert.Multiple(() =>
            {
                Assert.That(outcome.CurrentSeriesBestOf, Is.EqualTo(1));
                Assert.That(outcome.CurrentSeriesOver, Is.True);
                Assert.That(outcome.HomeHeadline, Is.EqualTo(5), "a Bo1 tiebreak reports its raw score");
            });
        }

        [Test]
        public void Tiebreak_FallsBackToTheMatchFormat_WhenNoneIsSet()
        {
            var outcome = Eval([G(1, 1), G(1, 0, series: 2)], TeamWinCondition.MatchWins, bestOf: 5);

            Assert.That(outcome.CurrentSeriesBestOf, Is.EqualTo(5), "a drawn Bo5 replays as another Bo5");
        }

        // ── Validation ─────────────────────────────────────────────────────────

        [Test]
        public void Validate_RejectsEmptySubmission()
        {
            Assert.Throws<BusinessRuleException>(
                () => SeriesEvaluator.ValidateAndEvaluate([], TeamWinCondition.MatchWins, 3, null, Localization));
        }

        [Test]
        public void Validate_RejectsMoreGamesThanTheFormatAllows()
        {
            var tooMany = new List<SeriesGame> { G(1, 0), G(1, 0), G(1, 0), G(1, 0) };

            Assert.Throws<BusinessRuleException>(
                () => SeriesEvaluator.ValidateAndEvaluate(tooMany, TeamWinCondition.MatchWins, 3, null, Localization));
        }

        // ── phase defaults ─────────────────────────────────────────────────────

        [Test]
        public void KnockoutBestOf_AppliesOnlyToTheBracketPhaseOfATwoPhaseTournament()
        {
            // Groups played as Bo1, knockout as Bo3.
            Assert.Multiple(() =>
            {
                Assert.That(
                    SeriesEvaluator.DefaultBestOfFor(TournamentFormat.GroupsThenSingleElimination, StageType.GroupStage, 1, 3),
                    Is.EqualTo(1), "the group stage keeps the tournament default");
                Assert.That(
                    SeriesEvaluator.DefaultBestOfFor(TournamentFormat.GroupsThenSingleElimination, StageType.SingleEliminationBracket, 1, 3),
                    Is.EqualTo(3), "the bracket that follows it plays the knockout format");
                Assert.That(
                    SeriesEvaluator.DefaultBestOfFor(TournamentFormat.Swiss, StageType.PlayIn, 1, 3),
                    Is.EqualTo(3), "a play-in is part of the knockout phase");
                Assert.That(
                    SeriesEvaluator.DefaultBestOfFor(TournamentFormat.Swiss, StageType.Swiss, 1, 3),
                    Is.EqualTo(1), "the Swiss rounds themselves are not");
            });
        }

        [Test]
        public void KnockoutBestOf_IsIgnoredWhereThereIsNoSeparateKnockoutPhase()
        {
            Assert.Multiple(() =>
            {
                Assert.That(
                    SeriesEvaluator.DefaultBestOfFor(TournamentFormat.SingleElimination, StageType.SingleEliminationBracket, 2, 5),
                    Is.EqualTo(2), "a plain bracket is one phase — BestOf already describes every match");
                Assert.That(
                    SeriesEvaluator.DefaultBestOfFor(TournamentFormat.League, StageType.League, 2, 5),
                    Is.EqualTo(2), "a league never plays a knockout match");
            });
        }

        [Test]
        public void NoKnockoutBestOf_MeansTheBracketPlaysLikeThePhaseBeforeIt()
        {
            Assert.That(
                SeriesEvaluator.DefaultBestOfFor(TournamentFormat.GroupsThenSingleElimination, StageType.SingleEliminationBracket, 3, null),
                Is.EqualTo(3), "every tournament created before the option behaves exactly as it did");
        }

        [Test]
        public void Validate_RejectsADeadRubberAfterTheClinch()
        {
            // Bo3 won 2-0: there is no third game to play, so one reported here never happened.
            var withDeadRubber = new List<SeriesGame> { G(1, 0), G(2, 1), G(0, 3) };

            Assert.Throws<BusinessRuleException>(
                () => SeriesEvaluator.ValidateAndEvaluate(withDeadRubber, TeamWinCondition.MatchWins, 3, null, Localization));
        }

        [Test]
        public void Validate_AcceptsEveryGameOfAnAggregateSeries()
        {
            // The mirror case: aggregate never clinches early, so all four games of a Bo4 stand
            // however lopsided the first ones were.
            var outcome = SeriesEvaluator.ValidateAndEvaluate(
                [G(5, 0), G(4, 0), G(0, 1), G(0, 2)],
                TeamWinCondition.AggregateScore,
                matchBestOf: 4,
                tiebreakBestOf: null,
                localization: Localization);

            Assert.Multiple(() =>
            {
                Assert.That(outcome.HomeHeadline, Is.EqualTo(9));
                Assert.That(outcome.AwayHeadline, Is.EqualTo(3));
                Assert.That(outcome.CurrentSeriesOver, Is.True);
            });
        }

        [Test]
        public void Validate_RejectsAnAggregateSeriesShortOfItsFullBestOf()
        {
            // Bo4 on aggregate is always four games — a 9-0 lead after two still has two to play.
            Assert.Throws<BusinessRuleException>(
                () => SeriesEvaluator.ValidateAndEvaluate(
                    [G(5, 0), G(4, 0)],
                    TeamWinCondition.AggregateScore,
                    matchBestOf: 4,
                    tiebreakBestOf: null,
                    localization: Localization));
        }

        [Test]
        public void Validate_RejectsUnfinishedSeries()
        {
            // 1–1 in a Bo3 still has a game to play.
            Assert.Throws<BusinessRuleException>(
                () => SeriesEvaluator.ValidateAndEvaluate([G(1, 0), G(0, 1)], TeamWinCondition.MatchWins, 3, null, Localization));
        }

        [Test]
        public void Validate_RejectsTiebreakAfterASeriesThatAlreadyHadAWinner()
        {
            // A tiebreak exists only because the series before it finished level.
            var games = new List<SeriesGame> { G(1, 0), G(1, 0), G(1, 0, series: 2) };

            Assert.Throws<BusinessRuleException>(
                () => SeriesEvaluator.ValidateAndEvaluate(games, TeamWinCondition.MatchWins, 3, null, Localization));
        }

        [Test]
        public void Validate_RejectsAGapInTheSeriesNumbering()
        {
            var games = new List<SeriesGame> { G(1, 1), G(1, 0, series: 3) };

            Assert.Throws<BusinessRuleException>(
                () => SeriesEvaluator.ValidateAndEvaluate(games, TeamWinCondition.MatchWins, 1, null, Localization));
        }

        [Test]
        public void Validate_RejectsNegativeScores()
        {
            Assert.Throws<BusinessRuleException>(
                () => SeriesEvaluator.ValidateAndEvaluate([G(-1, 0)], TeamWinCondition.MatchWins, 1, null, Localization));
        }

        [Test]
        public void Validate_AcceptsACompleteSeries()
        {
            var outcome = SeriesEvaluator.ValidateAndEvaluate(
                [G(2, 1), G(3, 0)], TeamWinCondition.MatchWins, 3, null, Localization);

            Assert.That(outcome.HomeHeadline, Is.EqualTo(2));
            Assert.That(outcome.AwayHeadline, Is.EqualTo(0));
        }

        // ── Format resolution ──────────────────────────────────────────────────

        [Test]
        public void Normalize_ClampsOutOfRangeValues()
        {
            Assert.Multiple(() =>
            {
                Assert.That(SeriesEvaluator.Normalize(null), Is.EqualTo(1));
                Assert.That(SeriesEvaluator.Normalize(0), Is.EqualTo(1));
                Assert.That(SeriesEvaluator.Normalize(-5), Is.EqualTo(1));
                Assert.That(SeriesEvaluator.Normalize(999), Is.EqualTo(SeriesEvaluator.MaxBestOf));
                Assert.That(SeriesEvaluator.Normalize(5), Is.EqualTo(5));
            });
        }

        [Test]
        public void Evaluate_OnAnUnplayedMatch_ReportsAnOpenFirstSeries()
        {
            var outcome = Eval([], TeamWinCondition.MatchWins, bestOf: 3);

            Assert.Multiple(() =>
            {
                Assert.That(outcome.CurrentSeriesNumber, Is.EqualTo(1));
                Assert.That(outcome.GamesInCurrentSeries, Is.EqualTo(0));
                Assert.That(outcome.CanAddGame, Is.True);
                Assert.That(outcome.CurrentSeriesOver, Is.False);
            });
        }
    }
}
