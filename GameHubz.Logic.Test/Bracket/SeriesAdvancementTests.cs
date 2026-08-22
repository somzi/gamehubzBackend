using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using NUnit.Framework;

using GameHubz.DataModels.Enums;
using GameHubz.DataModels.Models;
using GameHubz.Logic.Exceptions;

namespace GameHubz.Logic.Test.Bracket
{
    // Best-of series driven through the real result path: advancement, standings and the level-series
    // tiebreak. SQLite harness, because everything here shares the advancement / advisory-lock path
    // with UpdateMatchResult.
    [TestFixture]
    internal sealed class SeriesAdvancementTests
    {
        private static SeriesGame G(int home, int away, int series = 1)
            => new() { HomeScore = home, AwayScore = away, SeriesNumber = series };

        [Test]
        public async Task Bo3_MatchWins_AdvancesTheSeriesWinner_AndStoresBothScores()
        {
            var harness = new BracketTestHarness(useSqlite: true);
            var tid = await harness.SeedSoloTournamentAsync(TournamentFormat.SingleElimination, 4, bestOf: 3);
            await harness.NewService().GenerateSingleEliminationBracket(tid);

            var semi = harness.Matches(tid).Where(m => m.RoundNumber == 1).OrderBy(m => m.MatchOrder).First();

            await harness.NewService().UpdateMatchSeriesResult(new MatchSeriesResultDto
            {
                MatchId = semi.Id!.Value,
                TournamentId = tid,
                Games = [G(3, 1), G(0, 2), G(2, 1)],
            });

            var saved = harness.Matches(tid).Single(m => m.Id == semi.Id);

            Assert.Multiple(() =>
            {
                Assert.That(saved.Status, Is.EqualTo(MatchStatus.Completed));
                Assert.That(saved.WinnerParticipantId, Is.EqualTo(semi.HomeParticipantId));
                Assert.That(saved.HomeUserScore, Is.EqualTo(2), "headline is games won under MatchWins");
                Assert.That(saved.AwayUserScore, Is.EqualTo(1));
                Assert.That(saved.HomeGoalsTotal, Is.EqualTo(5), "3+0+2 real goals");
                Assert.That(saved.AwayGoalsTotal, Is.EqualTo(4), "1+2+1");
                Assert.That(saved.Games.Count, Is.EqualTo(3));
                Assert.That(saved.BestOf, Is.EqualTo(3), "the inherited format is frozen onto the match");
            });

            // The winner still advances exactly as a single-game result would.
            var final = harness.Matches(tid).Single(m => m.Id == semi.NextMatchId);
            Assert.That(
                final.HomeParticipantId == semi.HomeParticipantId || final.AwayParticipantId == semi.HomeParticipantId,
                Is.True,
                "series winner should occupy a slot in the next match");
        }

        [Test]
        public async Task Bo5_ClinchedEarly_AcceptsFewerGamesThanTheFormat()
        {
            var harness = new BracketTestHarness(useSqlite: true);
            var tid = await harness.SeedSoloTournamentAsync(TournamentFormat.SingleElimination, 4, bestOf: 5);
            await harness.NewService().GenerateSingleEliminationBracket(tid);

            var semi = harness.Matches(tid).Where(m => m.RoundNumber == 1).OrderBy(m => m.MatchOrder).First();

            // 3–0 in a Bo5 cannot be caught, so three games is a complete submission.
            await harness.NewService().UpdateMatchSeriesResult(new MatchSeriesResultDto
            {
                MatchId = semi.Id!.Value,
                TournamentId = tid,
                Games = [G(1, 0), G(2, 1), G(3, 2)],
            });

            var saved = harness.Matches(tid).Single(m => m.Id == semi.Id);

            Assert.That(saved.Status, Is.EqualTo(MatchStatus.Completed));
            Assert.That(saved.HomeUserScore, Is.EqualTo(3));
            Assert.That(saved.AwayUserScore, Is.EqualTo(0));
        }

        [Test]
        public async Task Knockout_LevelSeries_ParksForATiebreak_ThenTheReplayDecidesIt()
        {
            var harness = new BracketTestHarness(useSqlite: true);
            var tid = await harness.SeedSoloTournamentAsync(TournamentFormat.SingleElimination, 4, bestOf: 3);
            await harness.NewService().GenerateSingleEliminationBracket(tid);

            var semi = harness.Matches(tid).Where(m => m.RoundNumber == 1).OrderBy(m => m.MatchOrder).First();

            // Every game drawn: 0–0 on wins, so the Bo3 finishes level even though it is odd.
            await harness.NewService().UpdateMatchSeriesResult(new MatchSeriesResultDto
            {
                MatchId = semi.Id!.Value,
                TournamentId = tid,
                Games = [G(1, 1), G(2, 2), G(0, 0)],
            });

            var parked = harness.Matches(tid).Single(m => m.Id == semi.Id);
            Assert.Multiple(() =>
            {
                Assert.That(parked.Status, Is.EqualTo(MatchStatus.TieBreakRequired));
                Assert.That(parked.WinnerParticipantId, Is.Null, "a level series decides nobody");
                Assert.That(parked.Games.Count, Is.EqualTo(3));
            });

            var finalBefore = harness.Matches(tid).Single(m => m.Id == semi.NextMatchId);
            Assert.That(
                finalBefore.HomeParticipantId != semi.HomeParticipantId && finalBefore.AwayParticipantId != semi.HomeParticipantId,
                Is.True,
                "nothing may advance out of an undecided match");

            // The replay is reported by re-sending the whole match: main series plus the tiebreak.
            await harness.NewService().UpdateMatchSeriesResult(new MatchSeriesResultDto
            {
                MatchId = semi.Id!.Value,
                TournamentId = tid,
                Games = [G(1, 1), G(2, 2), G(0, 0), G(3, 1, series: 2), G(2, 0, series: 2)],
            });

            var decided = harness.Matches(tid).Single(m => m.Id == semi.Id);
            Assert.Multiple(() =>
            {
                Assert.That(decided.Status, Is.EqualTo(MatchStatus.Completed));
                Assert.That(decided.WinnerParticipantId, Is.EqualTo(semi.HomeParticipantId));
                Assert.That(decided.HomeUserScore, Is.EqualTo(2), "headline follows the deciding series");
                Assert.That(decided.AwayUserScore, Is.EqualTo(0));
                Assert.That(decided.HomeGoalsTotal, Is.EqualTo(8), "goals count every series: 1+2+0+3+2");
            });

            var finalAfter = harness.Matches(tid).Single(m => m.Id == semi.NextMatchId);
            Assert.That(
                finalAfter.HomeParticipantId == semi.HomeParticipantId || finalAfter.AwayParticipantId == semi.HomeParticipantId,
                Is.True,
                "the tiebreak winner advances");
        }

        [Test]
        public async Task League_LevelSeries_IsARealDraw_NotATiebreak()
        {
            var harness = new BracketTestHarness(useSqlite: true);
            var tid = await harness.SeedSoloTournamentAsync(TournamentFormat.League, 4, bestOf: 3);
            await harness.NewService().GenerateLeagueTournament(tid);

            // Round 1 only: later league rounds carry an open-at lock until the previous round finishes.
            var fixture = harness.Matches(tid)
                .First(m => m.RoundNumber == 1 && m.HomeParticipantId.HasValue && m.AwayParticipantId.HasValue);

            await harness.NewService().UpdateMatchSeriesResult(new MatchSeriesResultDto
            {
                MatchId = fixture.Id!.Value,
                TournamentId = tid,
                Games = [G(1, 0), G(0, 1), G(2, 2)],
            });

            var saved = harness.Matches(tid).Single(m => m.Id == fixture.Id);

            Assert.Multiple(() =>
            {
                Assert.That(saved.Status, Is.EqualTo(MatchStatus.Completed), "league keeps a level series as a draw");
                Assert.That(saved.WinnerParticipantId, Is.Null);
            });

            // A draw is a point each, and goal difference counts the real goals — never the win tally.
            var home = harness.Participants(tid).Single(p => p.Id == fixture.HomeParticipantId);
            var away = harness.Participants(tid).Single(p => p.Id == fixture.AwayParticipantId);

            Assert.Multiple(() =>
            {
                Assert.That(home.Points, Is.EqualTo(1));
                Assert.That(away.Points, Is.EqualTo(1));
                Assert.That(home.Draws, Is.EqualTo(1));
                Assert.That(home.GoalsFor, Is.EqualTo(3), "1+0+2 goals, not the 1–1 win tally");
                Assert.That(home.GoalsAgainst, Is.EqualTo(3));
            });
        }

        [Test]
        public async Task League_MatchWins_StandingsCountRealGoals_NotTheHeadline()
        {
            var harness = new BracketTestHarness(useSqlite: true);
            var tid = await harness.SeedSoloTournamentAsync(TournamentFormat.League, 4, bestOf: 3);
            await harness.NewService().GenerateLeagueTournament(tid);

            // Round 1 only: later league rounds carry an open-at lock until the previous round finishes.
            var fixture = harness.Matches(tid)
                .First(m => m.RoundNumber == 1 && m.HomeParticipantId.HasValue && m.AwayParticipantId.HasValue);

            // Home wins the series 2–1 while scoring 9 and conceding 4 across the games.
            await harness.NewService().UpdateMatchSeriesResult(new MatchSeriesResultDto
            {
                MatchId = fixture.Id!.Value,
                TournamentId = tid,
                Games = [G(5, 0), G(1, 3), G(3, 1)],
            });

            var home = harness.Participants(tid).Single(p => p.Id == fixture.HomeParticipantId);
            var away = harness.Participants(tid).Single(p => p.Id == fixture.AwayParticipantId);

            Assert.Multiple(() =>
            {
                Assert.That(home.Points, Is.EqualTo(3));
                Assert.That(home.Wins, Is.EqualTo(1));
                Assert.That(home.GoalsFor, Is.EqualTo(9), "would be 2 if the headline leaked into GF");
                Assert.That(home.GoalsAgainst, Is.EqualTo(4));
                Assert.That(away.GoalsFor, Is.EqualTo(4));
                Assert.That(away.Losses, Is.EqualTo(1));
            });
        }

        [Test]
        public async Task Aggregate_DecidesOnTotalScore_EvenWhenTheOtherSideWonMoreGames()
        {
            var harness = new BracketTestHarness(useSqlite: true);
            var tid = await harness.SeedSoloTournamentAsync(
                TournamentFormat.SingleElimination, 4,
                bestOf: 2, seriesWinCondition: TeamWinCondition.AggregateScore);
            await harness.NewService().GenerateSingleEliminationBracket(tid);

            var semi = harness.Matches(tid).Where(m => m.RoundNumber == 1).OrderBy(m => m.MatchOrder).First();

            // Away wins one game to nil but loses the tie on aggregate — the two criteria really differ.
            await harness.NewService().UpdateMatchSeriesResult(new MatchSeriesResultDto
            {
                MatchId = semi.Id!.Value,
                TournamentId = tid,
                Games = [G(5, 0), G(0, 3)],
            });

            var saved = harness.Matches(tid).Single(m => m.Id == semi.Id);

            Assert.Multiple(() =>
            {
                Assert.That(saved.HomeUserScore, Is.EqualTo(5), "aggregate headline IS the goal total");
                Assert.That(saved.AwayUserScore, Is.EqualTo(3));
                Assert.That(saved.WinnerParticipantId, Is.EqualTo(semi.HomeParticipantId));
            });
        }

        [Test]
        public async Task LegacySingleScoreReport_IsRefusedOnASeriesMatch()
        {
            var harness = new BracketTestHarness(useSqlite: true);
            var tid = await harness.SeedSoloTournamentAsync(TournamentFormat.SingleElimination, 4, bestOf: 3);
            await harness.NewService().GenerateSingleEliminationBracket(tid);

            var semi = harness.Matches(tid).Where(m => m.RoundNumber == 1).OrderBy(m => m.MatchOrder).First();

            // v1 can only describe one game. Recording a Bo3 as a single score would silently lose
            // the series, so it is refused with a message telling the client to update.
            Assert.ThrowsAsync<BusinessRuleException>(async () =>
                await harness.NewService().UpdateMatchResult(new MatchResultDto
                {
                    MatchId = semi.Id!.Value,
                    TournamentId = tid,
                    HomeScore = 2,
                    AwayScore = 1,
                }));
        }

        [Test]
        public async Task Bo1_Tournament_StillAcceptsTheLegacySingleScorePath()
        {
            // Every tournament that predates the feature runs at Bo1 — v1 must stay untouched there.
            var harness = new BracketTestHarness(useSqlite: true);
            var tid = await harness.SeedSoloTournamentAsync(TournamentFormat.SingleElimination, 4);
            await harness.NewService().GenerateSingleEliminationBracket(tid);

            var semi = harness.Matches(tid).Where(m => m.RoundNumber == 1).OrderBy(m => m.MatchOrder).First();

            await harness.NewService().UpdateMatchResult(new MatchResultDto
            {
                MatchId = semi.Id!.Value,
                TournamentId = tid,
                HomeScore = 3,
                AwayScore = 1,
            });

            var saved = harness.Matches(tid).Single(m => m.Id == semi.Id);

            Assert.Multiple(() =>
            {
                Assert.That(saved.Status, Is.EqualTo(MatchStatus.Completed));
                Assert.That(saved.HomeUserScore, Is.EqualTo(3), "a Bo1 headline is the score played");
                Assert.That(saved.AwayUserScore, Is.EqualTo(1));
                Assert.That(saved.WinnerParticipantId, Is.EqualTo(semi.HomeParticipantId));
            });
        }

        [Test]
        public async Task DeletingASeriesResult_ClearsTheGamesAndReopensTheMatch()
        {
            var harness = new BracketTestHarness(useSqlite: true);
            var tid = await harness.SeedSoloTournamentAsync(TournamentFormat.SingleElimination, 4, bestOf: 3);
            await harness.NewService().GenerateSingleEliminationBracket(tid);

            var semi = harness.Matches(tid).Where(m => m.RoundNumber == 1).OrderBy(m => m.MatchOrder).First();

            await harness.NewService().UpdateMatchSeriesResult(new MatchSeriesResultDto
            {
                MatchId = semi.Id!.Value,
                TournamentId = tid,
                Games = [G(2, 0), G(1, 0)],
            });

            await harness.NewService().RevertMatchResult(semi.Id!.Value);

            var reopened = harness.Matches(tid).Single(m => m.Id == semi.Id);

            Assert.Multiple(() =>
            {
                Assert.That(reopened.Status, Is.EqualTo(MatchStatus.Scheduled));
                Assert.That(reopened.GamesJson, Is.Null, "clearing the games also lifts the format lock");
                Assert.That(reopened.HomeUserScore, Is.Null);
                Assert.That(reopened.HomeGoalsTotal, Is.Null);
                Assert.That(reopened.WinnerParticipantId, Is.Null);
            });
        }

        [Test]
        public async Task ATiebreakPendingResult_CanBeDeleted()
        {
            var harness = new BracketTestHarness(useSqlite: true);
            var tid = await harness.SeedSoloTournamentAsync(TournamentFormat.SingleElimination, 4, bestOf: 3);
            await harness.NewService().GenerateSingleEliminationBracket(tid);

            var semi = harness.Matches(tid).Where(m => m.RoundNumber == 1).OrderBy(m => m.MatchOrder).First();

            await harness.NewService().UpdateMatchSeriesResult(new MatchSeriesResultDto
            {
                MatchId = semi.Id!.Value,
                TournamentId = tid,
                Games = [G(1, 1), G(2, 2), G(0, 0)],
            });

            await harness.NewService().RevertMatchResult(semi.Id!.Value);

            var reopened = harness.Matches(tid).Single(m => m.Id == semi.Id);

            Assert.That(reopened.Status, Is.EqualTo(MatchStatus.Scheduled));
            Assert.That(reopened.GamesJson, Is.Null);
        }
    }
}
