using System;
using System.Linq;
using System.Threading.Tasks;

using NUnit.Framework;

using GameHubz.DataModels.Enums;

namespace GameHubz.Logic.Test.Bracket
{
    [TestFixture]
    internal sealed class DoubleEliminationGenerationTests
    {
        // For a power-of-two bracket of N: WB = N-1, LB = N-2, Grand Final = 1, total = 2N-2.
        [TestCase(4)]
        [TestCase(8)]
        [TestCase(16)]
        public async Task PowerOfTwo_HasWinnersLosersAndGrandFinal(int players)
        {
            var harness = new BracketTestHarness();
            var tournamentId = await harness.SeedSoloTournamentAsync(TournamentFormat.DoubleElimination, players);

            await harness.Service.GenerateDoubleEliminationBracket(tournamentId);

            var matches = harness.Matches(tournamentId);

            int grandFinals = matches.Count(m => m.Stage == MatchStage.GrandFinal);
            int losersBracket = matches.Count(m => !m.IsUpperBracket);
            int winnersBracket = matches.Count(m => m.IsUpperBracket && m.Stage != MatchStage.GrandFinal);

            Assert.That(grandFinals, Is.EqualTo(1), "exactly one grand final");
            Assert.That(winnersBracket, Is.EqualTo(players - 1), "winners bracket has N-1 matches");
            Assert.That(losersBracket, Is.EqualTo(players - 2), "losers bracket has N-2 matches");
            Assert.That(matches.Count, Is.EqualTo(2 * players - 2), "total is 2N-2");
            Assert.That(matches.Count(m => m.Status == MatchStatus.Completed), Is.EqualTo(0), "no byes for power-of-two");
        }

        [TestCase(4)]
        [TestCase(8)]
        public async Task TwoStages_WinnersAndLosers(int players)
        {
            var harness = new BracketTestHarness();
            var tournamentId = await harness.SeedSoloTournamentAsync(TournamentFormat.DoubleElimination, players);

            await harness.Service.GenerateDoubleEliminationBracket(tournamentId);

            var stageTypes = harness.Stages(tournamentId).Select(s => s.Type).ToList();
            Assert.That(stageTypes, Does.Contain(StageType.DoubleEliminationWinnersBracket));
            Assert.That(stageTypes, Does.Contain(StageType.DoubleEliminationLosersBracket));
        }

        [Test]
        public async Task GrandFinalIsFedByWinnersFinalAndLosersFinal()
        {
            var harness = new BracketTestHarness();
            var tournamentId = await harness.SeedSoloTournamentAsync(TournamentFormat.DoubleElimination, 8);

            await harness.Service.GenerateDoubleEliminationBracket(tournamentId);

            var matches = harness.Matches(tournamentId);
            var grandFinal = matches.Single(m => m.Stage == MatchStage.GrandFinal);

            var feeders = matches.Where(m => m.NextMatchId == grandFinal.Id).ToList();
            Assert.That(feeders.Count, Is.EqualTo(2), "grand final is fed by two finals");
            Assert.That(feeders.Any(m => m.IsUpperBracket), Is.True, "one feeder from the winners bracket");
            Assert.That(feeders.Any(m => !m.IsUpperBracket), Is.True, "one feeder from the losers bracket");
        }

        // Non-power-of-two pads up; the LB bye-cascade may complete some empty LB matches but the
        // bracket must still be coherent: one grand final, every Pending match eventually fillable.
        [TestCase(5)]
        [TestCase(6)]
        [TestCase(7)]
        public async Task NonPowerOfTwo_StaysCoherent(int players)
        {
            var harness = new BracketTestHarness();
            var tournamentId = await harness.SeedSoloTournamentAsync(TournamentFormat.DoubleElimination, players);

            await harness.Service.GenerateDoubleEliminationBracket(tournamentId);

            var matches = harness.Matches(tournamentId);
            Assert.That(matches.Count(m => m.Stage == MatchStage.GrandFinal), Is.EqualTo(1), "still exactly one grand final");
            Assert.That(matches.Any(m => !m.IsUpperBracket), Is.True, "still has a losers bracket");
        }

        [TestCase(2)]
        [TestCase(3)]
        public async Task FewerThanFourParticipants_Throws(int players)
        {
            var harness = new BracketTestHarness();
            var tournamentId = await harness.SeedSoloTournamentAsync(TournamentFormat.DoubleElimination, players);

            Assert.That(async () => await harness.Service.GenerateDoubleEliminationBracket(tournamentId),
                Throws.Exception);
        }
    }
}
