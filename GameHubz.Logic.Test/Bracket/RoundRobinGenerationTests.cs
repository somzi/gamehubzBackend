using System.Linq;
using System.Threading.Tasks;

using NUnit.Framework;

using GameHubz.DataModels.Enums;

namespace GameHubz.Logic.Test.Bracket
{
    [TestFixture]
    internal sealed class RoundRobinGenerationTests
    {
        // A single round-robin of N players has C(N,2) = N(N-1)/2 matches; every pair meets once.
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        [TestCase(6)]
        [TestCase(8)]
        public async Task League_SingleRoundRobin_HasAllPairings(int players)
        {
            int expected = players * (players - 1) / 2;

            var harness = new BracketTestHarness();
            var tournamentId = await harness.SeedSoloTournamentAsync(TournamentFormat.League, players);

            await harness.Service.GenerateLeagueTournament(tournamentId);

            var matches = harness.Matches(tournamentId);
            Assert.That(matches.Count, Is.EqualTo(expected), "C(N,2) fixtures");
            Assert.That(matches.All(m => m.Stage == MatchStage.GroupStage), Is.True, "league fixtures are group-stage");

            AssertEachUnorderedPairAppearsOnce(matches, expected);
        }

        [TestCase(4)]
        [TestCase(5)]
        [TestCase(6)]
        public async Task League_DoubleRoundRobin_DoublesTheFixtures(int players)
        {
            int expected = players * (players - 1);

            var harness = new BracketTestHarness();
            var tournamentId = await harness.SeedSoloTournamentAsync(TournamentFormat.League, players, doubleRoundRobin: true);

            await harness.Service.GenerateLeagueTournament(tournamentId, doubleRoundRobin: true);

            var matches = harness.Matches(tournamentId);
            Assert.That(matches.Count, Is.EqualTo(expected), "N(N-1) fixtures for a double round-robin");
        }

        [Test]
        public async Task League_CreatesOneLeagueStageAndGroupAndAssignsParticipants()
        {
            var harness = new BracketTestHarness();
            var tournamentId = await harness.SeedSoloTournamentAsync(TournamentFormat.League, 6);

            await harness.Service.GenerateLeagueTournament(tournamentId);

            var stages = harness.Stages(tournamentId);
            Assert.That(stages.Count, Is.EqualTo(1));
            Assert.That(stages[0].Type, Is.EqualTo(StageType.League));

            var groups = harness.Groups(tournamentId);
            Assert.That(groups.Count, Is.EqualTo(1), "one league table");

            var participants = harness.Participants(tournamentId);
            Assert.That(participants.All(p => p.TournamentGroupId == groups[0].Id), Is.True,
                "every participant is assigned to the league table");
            Assert.That(participants.All(p => p.Points == 0 && p.Wins == 0 && p.Losses == 0), Is.True,
                "standings are reset at generation");
        }

        private static void AssertEachUnorderedPairAppearsOnce(
            System.Collections.Generic.List<GameHubz.DataModels.Domain.MatchEntity> matches, int expected)
        {
            var pairs = matches
                .Select(m =>
                {
                    var a = m.HomeParticipantId!.Value;
                    var b = m.AwayParticipantId!.Value;
                    return a.CompareTo(b) < 0 ? (a, b) : (b, a);
                })
                .ToList();

            Assert.That(pairs.Distinct().Count(), Is.EqualTo(expected), "no duplicate fixtures");
        }
    }
}
