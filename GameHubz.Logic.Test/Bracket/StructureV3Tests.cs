using System.Linq;
using System.Threading.Tasks;

using NUnit.Framework;

using GameHubz.DataModels.Enums;

namespace GameHubz.Logic.Test.Bracket
{
    // GetTournamentStructureV3: for team tournaments the group-stage cards are one Team-vs-Team
    // entry per TeamMatch (v1/v2 keep emitting the per-player sub-match cards for legacy clients);
    // for solo tournaments v3 must stay shape-identical to v1. In-memory harness (read path).
    [TestFixture]
    internal sealed class StructureV3Tests
    {
        [Test]
        public async Task V3_TeamGroupStage_ReturnsOneCardPerTeamFixture_V2KeepsSubMatchCards()
        {
            const int teamSize = 2;

            var harness = new BracketTestHarness();
            var tournamentId = await harness.SeedTeamTournamentAsync(TournamentFormat.GroupStageWithKnockout, 8, teamSize);
            await harness.Service.GenerateTeamGroupStageWithKnockout(tournamentId, numberOfGroups: 2, qualifiersPerGroup: 2);

            // 2 groups x C(4,2) = 6 team fixtures per group.
            var v3 = await harness.NewService().GetTournamentStructureV3(tournamentId);
            var v3Groups = v3.Stages.Single(s => s.Type == StageType.GroupStage).Groups!;
            Assert.That(v3Groups.Count, Is.EqualTo(2));
            foreach (var group in v3Groups)
            {
                Assert.That(group.Matches.Count, Is.EqualTo(6), "v3: one card per team fixture");
                Assert.That(group.Matches.All(m => m.TeamMatchId.HasValue), Is.True,
                    "v3 group cards are the TeamMatch cards");
            }

            var v2 = await harness.NewService().GetTournamentStructureV2(tournamentId);
            var v2Groups = v2.Stages.Single(s => s.Type == StageType.GroupStage).Groups!;
            foreach (var group in v2Groups)
            {
                Assert.That(group.Matches.Count, Is.EqualTo(6 * teamSize),
                    "v2 keeps the per-player sub-match cards byte-compatible for old clients");
            }
        }

        [Test]
        public async Task V3_SoloBracket_IsShapeIdenticalToV1()
        {
            var harness = new BracketTestHarness();
            var tournamentId = await harness.SeedSoloTournamentAsync(TournamentFormat.SingleElimination, 8);
            await harness.Service.GenerateSingleEliminationBracket(tournamentId);

            var v1 = await harness.NewService().GetTournamentStructure(tournamentId);
            var v3 = await harness.NewService().GetTournamentStructureV3(tournamentId);

            Assert.That(v3.Stages.Count, Is.EqualTo(v1.Stages.Count));
            Assert.That(v3.Stages.Single().Rounds!.Count, Is.EqualTo(v1.Stages.Single().Rounds!.Count));
            Assert.That(
                v3.Stages.Single().Rounds!.Sum(r => r.Matches.Count),
                Is.EqualTo(v1.Stages.Single().Rounds!.Sum(r => r.Matches.Count)),
                "solo tournaments are untouched by the v3 team-card change");
        }
    }
}
