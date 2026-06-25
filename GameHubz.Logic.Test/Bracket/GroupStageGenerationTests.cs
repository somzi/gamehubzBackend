using System.Linq;
using System.Threading.Tasks;

using NUnit.Framework;

using GameHubz.DataModels.Enums;

namespace GameHubz.Logic.Test.Bracket
{
    [TestFixture]
    internal sealed class GroupStageGenerationTests
    {
        // 2 groups of 4, top 2 advance -> 4 qualifiers (power of two). Each group is a round-robin
        // of 4 = 6 fixtures, so 12 group-stage matches. The knockout stage is created empty; its
        // matches are filled in once the groups finish.
        [Test]
        public async Task TwoGroupsOfFour_BuildsGroupRoundRobinsAndEmptyKnockout()
        {
            var harness = new BracketTestHarness();
            var tournamentId = await harness.SeedSoloTournamentAsync(TournamentFormat.GroupStageWithKnockout, 8);

            await harness.Service.GenerateGroupStageWithKnockout(tournamentId, numberOfGroups: 2, qualifiersPerGroup: 2);

            var stages = harness.Stages(tournamentId);
            Assert.That(stages.Select(s => s.Type), Does.Contain(StageType.GroupStage));
            Assert.That(stages.Select(s => s.Type), Does.Contain(StageType.SingleEliminationBracket));

            var groups = harness.Groups(tournamentId);
            Assert.That(groups.Count, Is.EqualTo(2), "two groups");

            var matches = harness.Matches(tournamentId);
            Assert.That(matches.Count, Is.EqualTo(12), "2 groups x C(4,2)=6 fixtures");
            Assert.That(matches.All(m => m.Stage == MatchStage.GroupStage), Is.True,
                "knockout matches are not generated until the groups finish");

            var participants = harness.Participants(tournamentId);
            Assert.That(participants.All(p => p.TournamentGroupId.HasValue), Is.True, "everyone is drawn into a group");
            foreach (var g in groups)
            {
                Assert.That(participants.Count(p => p.TournamentGroupId == g.Id), Is.EqualTo(4),
                    "balanced 4-player groups");
            }
        }

        [Test]
        public async Task QualifiersNotPowerOfTwo_Throws()
        {
            var harness = new BracketTestHarness();
            var tournamentId = await harness.SeedSoloTournamentAsync(TournamentFormat.GroupStageWithKnockout, 8);

            // 2 groups x 3 qualifiers = 6, not a power of two.
            Assert.That(async () => await harness.Service.GenerateGroupStageWithKnockout(tournamentId, 2, 3),
                Throws.Exception);
        }

        [Test]
        public async Task NotEnoughParticipantsForGroups_Throws()
        {
            var harness = new BracketTestHarness();
            var tournamentId = await harness.SeedSoloTournamentAsync(TournamentFormat.GroupStageWithKnockout, 3);

            // 2 groups need at least 4 players.
            Assert.That(async () => await harness.Service.GenerateGroupStageWithKnockout(tournamentId, 2, 2),
                Throws.Exception);
        }

        [Test]
        public async Task DoubleEliminationKnockout_CreatesWinnersAndLosersStages()
        {
            var harness = new BracketTestHarness();
            var tournamentId = await harness.SeedSoloTournamentAsync(
                TournamentFormat.GroupStageWithKnockout, 8,
                knockoutEliminationType: KnockoutEliminationType.Double);

            await harness.Service.GenerateGroupStageWithKnockout(tournamentId, numberOfGroups: 2, qualifiersPerGroup: 2);

            var stageTypes = harness.Stages(tournamentId).Select(s => s.Type).ToList();
            Assert.That(stageTypes, Does.Contain(StageType.GroupStage));
            Assert.That(stageTypes, Does.Contain(StageType.DoubleEliminationWinnersBracket));
            Assert.That(stageTypes, Does.Contain(StageType.DoubleEliminationLosersBracket));
        }
    }
}
