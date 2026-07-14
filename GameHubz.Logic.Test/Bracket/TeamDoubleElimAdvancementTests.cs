using System;
using System.Linq;
using System.Threading.Tasks;

using NUnit.Framework;

using GameHubz.DataModels.Enums;
using GameHubz.DataModels.Models;

namespace GameHubz.Logic.Test.Bracket
{
    // Team double-elimination progression: WB team losers drop into the team Losers Bracket (and
    // the drop target's sub-matches materialize once both sides are in), the whole bracket plays
    // through to a champion, and an LB-champion Grand Final win forces the team reset final.
    // SQLite harness; every result flows through sub-match reports like production.
    [TestFixture]
    internal sealed class TeamDoubleElimAdvancementTests
    {
        private const int TeamSize = 2;

        [Test]
        public async Task WbTeamLoser_DropsToLosersBracket_AndItsSubMatchesMaterialize()
        {
            var harness = new BracketTestHarness(useSqlite: true);
            var tournamentId = await harness.SeedTeamTournamentAsync(TournamentFormat.DoubleElimination, 4, TeamSize);
            await harness.NewService().GenerateTeamDoubleEliminationBracket(tournamentId);

            var wbRound1 = harness.TeamMatches(tournamentId)
                .Where(tm => tm.IsUpperBracket && !tm.IsGrandFinal && tm.RoundNumber == 1)
                .ToList();
            Assert.That(wbRound1.Count, Is.EqualTo(2), "4 teams -> 2 WB semifinal fixtures");

            await ReportFixtureAsync(harness, tournamentId, wbRound1[0].Id!.Value, homeWins: true);

            var decided = harness.TeamMatches(tournamentId).Single(tm => tm.Id == wbRound1[0].Id);
            Assert.That(decided.Status, Is.EqualTo(TeamMatchStatus.Completed));

            var lbTarget = harness.TeamMatches(tournamentId).Single(tm => tm.Id == wbRound1[0].NextTeamMatchLoserBracketId);
            bool loserDropped = lbTarget.HomeTeamParticipantId == wbRound1[0].AwayTeamParticipantId
                || lbTarget.AwayTeamParticipantId == wbRound1[0].AwayTeamParticipantId;
            Assert.That(loserDropped, Is.True, "the losing team fell into the Losers Bracket fixture");
            Assert.That(harness.Matches(tournamentId).Count(m => m.TeamMatchId == lbTarget.Id),
                Is.EqualTo(0), "LB fixture still waits for its second team");

            await ReportFixtureAsync(harness, tournamentId, wbRound1[1].Id!.Value, homeWins: true);

            Assert.That(harness.Matches(tournamentId).Count(m => m.TeamMatchId == lbTarget.Id),
                Is.EqualTo(TeamSize), "both losers in -> the LB fixture's sub-matches materialize");
        }

        [Test]
        public async Task FullPlaythrough_WbChampionWinsGrandFinal_CompletesWithTeamChampion()
        {
            var harness = new BracketTestHarness(useSqlite: true);
            var tournamentId = await harness.SeedTeamTournamentAsync(TournamentFormat.DoubleElimination, 4, TeamSize);
            await harness.NewService().GenerateTeamDoubleEliminationBracket(tournamentId);

            await PlayOutAsync(harness, tournamentId, grandFinalHomeWins: true);

            var tournament = harness.Tournament(tournamentId);
            Assert.That(tournament.Status, Is.EqualTo(TournamentStatus.Completed), "bracket played to a champion");

            var grandFinal = harness.TeamMatches(tournamentId).Single(tm => tm.IsGrandFinal);
            var champion = harness.Participants(tournamentId).Single(p => p.Id == grandFinal.WinnerTeamParticipantId);
            Assert.That(tournament.WinnerTeamId, Is.EqualTo(champion.TeamId), "champion team recorded");

            Assert.That(harness.TeamMatches(tournamentId).Any(tm => tm.IsGrandFinalReset),
                Is.False, "WB champion won -> no reset fixture");
        }

        [Test]
        public async Task GrandFinal_LbChampionWin_ForcesTeamResetFinal_WhoseWinnerTakesTheTitle()
        {
            var harness = new BracketTestHarness(useSqlite: true);
            var tournamentId = await harness.SeedTeamTournamentAsync(TournamentFormat.DoubleElimination, 4, TeamSize);
            await harness.NewService().GenerateTeamDoubleEliminationBracket(tournamentId);

            await PlayOutAsync(harness, tournamentId, grandFinalHomeWins: false);

            var reset = harness.TeamMatches(tournamentId).SingleOrDefault(tm => tm.IsGrandFinalReset);
            Assert.That(reset, Is.Not.Null, "LB-champion win forces the reset fixture");
            Assert.That(harness.Tournament(tournamentId).Status, Is.Not.EqualTo(TournamentStatus.Completed),
                "the title waits for the reset");

            var resetSubMatches = harness.Matches(tournamentId).Where(m => m.TeamMatchId == reset!.Id).ToList();
            Assert.That(resetSubMatches.Count, Is.EqualTo(TeamSize), "reset sub-matches built immediately");

            await ReportFixtureAsync(harness, tournamentId, reset!.Id!.Value, homeWins: true);

            var tournament = harness.Tournament(tournamentId);
            Assert.That(tournament.Status, Is.EqualTo(TournamentStatus.Completed), "reset decides the title");

            var decidedReset = harness.TeamMatches(tournamentId).Single(tm => tm.Id == reset.Id);
            var champion = harness.Participants(tournamentId).Single(p => p.Id == decidedReset.WinnerTeamParticipantId);
            Assert.That(tournament.WinnerTeamId, Is.EqualTo(champion.TeamId), "champion = reset winner");
        }

        // Reports every sub-match of one fixture, home or away sweeping it.
        private static async Task ReportFixtureAsync(BracketTestHarness harness, Guid tournamentId, Guid teamMatchId, bool homeWins)
        {
            var subMatches = harness.Matches(tournamentId).Where(m => m.TeamMatchId == teamMatchId).ToList();
            foreach (var sm in subMatches)
            {
                await harness.NewService().UpdateMatchResult(new MatchResultDto
                {
                    MatchId = sm.Id!.Value,
                    TournamentId = tournamentId,
                    HomeScore = homeWins ? 2 : 1,
                    AwayScore = homeWins ? 1 : 2,
                });
            }
        }

        // Sweeps every playable sub-match home-win, except the Grand Final fixture whose side is
        // chosen by the caller (home = WB champion, away = LB champion). Stops after an away-win
        // GF so the test can drive the freshly created reset fixture explicitly.
        private static async Task PlayOutAsync(BracketTestHarness harness, Guid tournamentId, bool grandFinalHomeWins)
        {
            for (int guard = 0; guard < 200; guard++)
            {
                var fixturesById = harness.TeamMatches(tournamentId).ToDictionary(tm => tm.Id!.Value);
                var playable = harness.Matches(tournamentId)
                    .Where(m => m.Status != MatchStatus.Completed && m.TeamMatchId.HasValue)
                    // Grand Final fixture last, so WB and LB both feed it first.
                    .OrderBy(m => fixturesById[m.TeamMatchId!.Value].IsGrandFinal ? 1 : 0)
                    .FirstOrDefault();
                if (playable == null) break;

                bool isGrandFinal = fixturesById[playable.TeamMatchId!.Value].IsGrandFinal;
                bool homeWins = !isGrandFinal || grandFinalHomeWins;

                await harness.NewService().UpdateMatchResult(new MatchResultDto
                {
                    MatchId = playable.Id!.Value,
                    TournamentId = tournamentId,
                    HomeScore = homeWins ? 2 : 1,
                    AwayScore = homeWins ? 1 : 2,
                });

                if (isGrandFinal && !grandFinalHomeWins
                    && harness.TeamMatches(tournamentId).Any(tm => tm.IsGrandFinalReset))
                {
                    return; // reset fixture exists; the test drives it explicitly
                }
            }
        }
    }
}
