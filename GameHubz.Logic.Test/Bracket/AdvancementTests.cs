using System.Linq;
using System.Threading.Tasks;

using NUnit.Framework;

using GameHubz.DataModels.Domain;
using GameHubz.DataModels.Enums;
using GameHubz.DataModels.Models;

namespace GameHubz.Logic.Test.Bracket
{
    // Advancement / result-processing runs on the SQLite harness: UpdateMatchResult takes a
    // per-tournament Postgres advisory lock (raw SQL, relational-only) that the in-memory provider
    // can't execute. SQLite gives a real relational provider; the lock functions are registered as no-ops.
    [TestFixture]
    internal sealed class AdvancementTests
    {
        [Test]
        public async Task ReportingResult_CompletesMatchAndAdvancesWinner()
        {
            var harness = new BracketTestHarness(useSqlite: true);
            var tournamentId = await harness.SeedSoloTournamentAsync(TournamentFormat.SingleElimination, 8);
            await harness.NewService().GenerateSingleEliminationBracket(tournamentId);

            var r1 = harness.Matches(tournamentId).First(m => m.RoundNumber == 1);

            await harness.NewService().UpdateMatchResult(new MatchResultDto
            {
                MatchId = r1.Id!.Value,
                TournamentId = tournamentId,
                HomeScore = 2,
                AwayScore = 1,
            });

            var completed = harness.Match(r1.Id!.Value);
            Assert.That(completed.Status, Is.EqualTo(MatchStatus.Completed));
            Assert.That(completed.WinnerParticipantId, Is.EqualTo(r1.HomeParticipantId), "higher score wins");
            Assert.That(completed.HomeUserScore, Is.EqualTo(2));
            Assert.That(completed.AwayUserScore, Is.EqualTo(1));

            // Winner is placed into the next match.
            var next = harness.Match(r1.NextMatchId!.Value);
            bool winnerPlaced = next.HomeParticipantId == r1.HomeParticipantId
                || next.AwayParticipantId == r1.HomeParticipantId;
            Assert.That(winnerPlaced, Is.True, "winner advanced into the next match slot");
        }

        [Test]
        public async Task BothFeeders_FillTheNextMatch()
        {
            var harness = new BracketTestHarness(useSqlite: true);
            var tournamentId = await harness.SeedSoloTournamentAsync(TournamentFormat.SingleElimination, 4);
            await harness.NewService().GenerateSingleEliminationBracket(tournamentId);

            // N=4: two semi-finals feed the one final.
            var semis = harness.Matches(tournamentId).Where(m => m.RoundNumber == 1).ToList();
            Assert.That(semis.Count, Is.EqualTo(2));

            foreach (var semi in semis)
            {
                await harness.NewService().UpdateMatchResult(new MatchResultDto
                {
                    MatchId = semi.Id!.Value,
                    TournamentId = tournamentId,
                    HomeScore = 3,
                    AwayScore = 0,
                });
            }

            var final = harness.Match(semis[0].NextMatchId!.Value);
            Assert.That(final.HomeParticipantId, Is.Not.Null, "final home slot filled");
            Assert.That(final.AwayParticipantId, Is.Not.Null, "final away slot filled");
        }

        [Test]
        public async Task RevertResult_ReopensMatchAndClearsNextSlot()
        {
            var harness = new BracketTestHarness(useSqlite: true);
            var tournamentId = await harness.SeedSoloTournamentAsync(TournamentFormat.SingleElimination, 4);
            await harness.NewService().GenerateSingleEliminationBracket(tournamentId);

            var semi = harness.Matches(tournamentId).First(m => m.RoundNumber == 1);
            var winnerId = semi.HomeParticipantId;

            await harness.NewService().UpdateMatchResult(new MatchResultDto
            {
                MatchId = semi.Id!.Value, TournamentId = tournamentId, HomeScore = 2, AwayScore = 1,
            });

            // Confirm it advanced, then revert.
            await harness.NewService().RevertMatchResult(semi.Id!.Value);

            var reverted = harness.Match(semi.Id!.Value);
            Assert.That(reverted.Status, Is.EqualTo(MatchStatus.Scheduled), "match reopened");
            Assert.That(reverted.WinnerParticipantId, Is.Null, "winner cleared");
            Assert.That(reverted.HomeUserScore, Is.Null, "score cleared");

            var final = harness.Match(semi.NextMatchId!.Value);
            bool slotCleared = final.HomeParticipantId != winnerId && final.AwayParticipantId != winnerId;
            Assert.That(slotCleared, Is.True, "winner removed from the next match");
        }

        [Test]
        public async Task RevertIsLocked_WhenDownstreamMatchProgressed()
        {
            var harness = new BracketTestHarness(useSqlite: true);
            var tournamentId = await harness.SeedSoloTournamentAsync(TournamentFormat.SingleElimination, 4);
            await harness.NewService().GenerateSingleEliminationBracket(tournamentId);

            var semis = harness.Matches(tournamentId).Where(m => m.RoundNumber == 1).ToList();
            foreach (var semi in semis)
            {
                await harness.NewService().UpdateMatchResult(new MatchResultDto
                {
                    MatchId = semi.Id!.Value, TournamentId = tournamentId, HomeScore = 2, AwayScore = 1,
                });
            }

            // Final now has both finalists; play it.
            var finalId = semis[0].NextMatchId!.Value;
            await harness.NewService().UpdateMatchResult(new MatchResultDto
            {
                MatchId = finalId, TournamentId = tournamentId, HomeScore = 1, AwayScore = 0,
            });

            // Reverting a semi-final is now locked — the final has progressed.
            Assert.That(async () => await harness.NewService().RevertMatchResult(semis[0].Id!.Value),
                Throws.Exception);
        }

        [Test]
        public async Task DrawInElimination_Throws()
        {
            var harness = new BracketTestHarness(useSqlite: true);
            var tournamentId = await harness.SeedSoloTournamentAsync(TournamentFormat.SingleElimination, 4);
            await harness.NewService().GenerateSingleEliminationBracket(tournamentId);

            var semi = harness.Matches(tournamentId).First(m => m.RoundNumber == 1);

            Assert.That(async () => await harness.NewService().UpdateMatchResult(new MatchResultDto
            {
                MatchId = semi.Id!.Value, TournamentId = tournamentId, HomeScore = 1, AwayScore = 1,
            }), Throws.Exception, "elimination needs a winner");
        }

        [Test]
        public async Task FullSingleEliminationPlaythrough_CompletesTournamentWithAChampion()
        {
            var harness = new BracketTestHarness(useSqlite: true);
            var tournamentId = await harness.SeedSoloTournamentAsync(TournamentFormat.SingleElimination, 8);
            await harness.NewService().GenerateSingleEliminationBracket(tournamentId);

            await PlayOutAllPendingSoloMatches(harness, tournamentId);

            var tournament = harness.Tournament(tournamentId);
            Assert.That(tournament.Status, Is.EqualTo(TournamentStatus.Completed), "tournament concluded");

            var final = harness.Matches(tournamentId).Single(m => m.Stage == MatchStage.Final);
            Assert.That(final.Status, Is.EqualTo(MatchStatus.Completed), "final played");
            Assert.That(final.WinnerParticipantId, Is.Not.Null, "champion decided");
        }

        [Test]
        public async Task LeagueStandings_ReflectReportedResults()
        {
            var harness = new BracketTestHarness(useSqlite: true);
            var tournamentId = await harness.SeedSoloTournamentAsync(TournamentFormat.League, 4);
            await harness.NewService().GenerateLeagueTournament(tournamentId);

            var match = harness.Matches(tournamentId).First(m => m.Stage == MatchStage.GroupStage);
            var homeId = match.HomeParticipantId!.Value;
            var awayId = match.AwayParticipantId!.Value;

            await harness.NewService().UpdateMatchResult(new MatchResultDto
            {
                MatchId = match.Id!.Value, TournamentId = tournamentId, HomeScore = 3, AwayScore = 1,
            });

            var standings = await harness.NewService().GetLeagueStandings(tournamentId);

            var home = standings.Single(s => s.ParticipantId == homeId);
            var away = standings.Single(s => s.ParticipantId == awayId);

            Assert.That(home.Points, Is.EqualTo(3), "win = 3 points");
            Assert.That(home.Wins, Is.EqualTo(1));
            Assert.That(home.GoalsFor, Is.EqualTo(3));
            Assert.That(home.GoalsAgainst, Is.EqualTo(1));
            Assert.That(away.Points, Is.EqualTo(0));
            Assert.That(away.Losses, Is.EqualTo(1));
            Assert.That(home.Position, Is.LessThan(away.Position), "winner ranks above loser");
        }

        /// <summary>
        /// Reports a 2-1 home win on every playable solo match (Pending with both participants), round
        /// after round, until the bracket is fully played. Each report uses a fresh service/context.
        /// </summary>
        private static async Task PlayOutAllPendingSoloMatches(BracketTestHarness harness, System.Guid tournamentId)
        {
            for (int guard = 0; guard < 100; guard++)
            {
                var playable = harness.Matches(tournamentId)
                    .FirstOrDefault(m => m.Status != MatchStatus.Completed
                        && m.HomeParticipantId.HasValue && m.AwayParticipantId.HasValue);

                if (playable == null) break;

                await harness.NewService().UpdateMatchResult(new MatchResultDto
                {
                    MatchId = playable.Id!.Value,
                    TournamentId = tournamentId,
                    HomeScore = 2,
                    AwayScore = 1,
                });
            }
        }
    }
}
