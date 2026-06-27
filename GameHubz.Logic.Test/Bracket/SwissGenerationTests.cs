using System.Linq;
using System.Threading.Tasks;

using NUnit.Framework;

using GameHubz.DataModels.Enums;

namespace GameHubz.Logic.Test.Bracket
{
    [TestFixture]
    internal sealed class SwissGenerationTests
    {
        // Swiss generates lazily: only round 1 exists at generation time. Even N -> N/2 paired
        // matches. Odd N -> (N-1)/2 paired matches plus one auto-completed bye match, so the count
        // is ceil(N/2) and there is exactly one Completed bye. Later rounds are paired as results come in.
        [TestCase(8)]
        [TestCase(6)]
        [TestCase(7)]
        [TestCase(5)]
        public async Task PureSwiss_GeneratesOnlyFirstRound(int players)
        {
            int expectedRound1 = (players + 1) / 2; // ceil(N/2)
            int expectedByes = players % 2; // odd -> 1 bye match, even -> 0

            var harness = new BracketTestHarness();
            var tournamentId = await harness.SeedSoloTournamentAsync(TournamentFormat.Swiss, players);

            await harness.Service.GenerateSwissTournament(tournamentId);

            var matches = harness.Matches(tournamentId);
            Assert.That(matches.Count, Is.EqualTo(expectedRound1), "only the first Swiss round is materialised");
            Assert.That(matches.All(m => m.RoundNumber == 1), Is.True, "all generated matches are round 1");

            var byes = matches.Where(m => m.Status == MatchStatus.Completed).ToList();
            Assert.That(byes.Count, Is.EqualTo(expectedByes), "odd fields produce one bye match");
            Assert.That(byes.All(b => b.WinnerParticipantId.HasValue && !b.AwayParticipantId.HasValue), Is.True,
                "the bye is a single-participant auto-win");
        }

        [Test]
        public async Task PureSwiss_HasSingleSwissStageAndStandingsGroup()
        {
            var harness = new BracketTestHarness();
            var tournamentId = await harness.SeedSoloTournamentAsync(TournamentFormat.Swiss, 8);

            await harness.Service.GenerateSwissTournament(tournamentId);

            var stages = harness.Stages(tournamentId);
            Assert.That(stages.Count, Is.EqualTo(1), "pure Swiss has one stage");
            Assert.That(stages[0].Type, Is.EqualTo(StageType.Swiss));

            var groups = harness.Groups(tournamentId);
            Assert.That(groups.Count, Is.EqualTo(1), "single standings group");

            var participants = harness.Participants(tournamentId);
            Assert.That(participants.All(p => p.TournamentGroupId == groups[0].Id), Is.True,
                "everyone is in the standings group");
            Assert.That(participants.Select(p => p.Seed).Distinct().Count(), Is.EqualTo(participants.Count),
                "seeds assigned uniquely (pairing/tiebreak order)");
        }

        [Test]
        public async Task SwissWithKnockout_PreCreatesKnockoutStage()
        {
            var harness = new BracketTestHarness();
            var tournamentId = await harness.SeedSoloTournamentAsync(
                TournamentFormat.Swiss, 8, swissKnockoutQualifiers: 4);

            await harness.Service.GenerateSwissTournament(tournamentId);

            var stageTypes = harness.Stages(tournamentId).Select(s => s.Type).ToList();
            Assert.That(stageTypes, Does.Contain(StageType.Swiss));
            Assert.That(stageTypes, Does.Contain(StageType.SingleEliminationBracket), "post-Swiss knockout stage exists");
        }

        [Test]
        public async Task SwissWithPlayIn_PreCreatesPlayInStage()
        {
            var harness = new BracketTestHarness();
            // 4 qualifiers, 2 direct -> 2 play-in slots decided between standings 3..6.
            var tournamentId = await harness.SeedSoloTournamentAsync(
                TournamentFormat.Swiss, 8, swissKnockoutQualifiers: 4, swissDirectQualifiers: 2);

            await harness.Service.GenerateSwissTournament(tournamentId);

            var stageTypes = harness.Stages(tournamentId).Select(s => s.Type).ToList();
            Assert.That(stageTypes, Does.Contain(StageType.PlayIn), "play-in stage exists when direct < qualifiers");
        }

        [Test]
        public async Task KnockoutQualifiersNotPowerOfTwo_Throws()
        {
            var harness = new BracketTestHarness();
            var tournamentId = await harness.SeedSoloTournamentAsync(
                TournamentFormat.Swiss, 8, swissKnockoutQualifiers: 3);

            Assert.That(async () => await harness.Service.GenerateSwissTournament(tournamentId),
                Throws.Exception);
        }
    }
}
