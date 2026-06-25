using System.Linq;
using System.Threading.Tasks;

using NUnit.Framework;

using GameHubz.DataModels.Enums;

namespace GameHubz.Logic.Test.Bracket
{
    // GetTournamentStructure / V2 read paths. These run on the in-memory provider — the structure
    // query's AsSplitQuery is silently ignored there, so no relational backend is needed.
    [TestFixture]
    internal sealed class StructureReadTests
    {
        [Test]
        public async Task GetTournamentStructure_SingleElimination_ReturnsRoundsAndMatches()
        {
            var harness = new BracketTestHarness();
            var tournamentId = await harness.SeedSoloTournamentAsync(TournamentFormat.SingleElimination, 8);
            await harness.NewService().GenerateSingleEliminationBracket(tournamentId);

            var structure = await harness.NewService().GetTournamentStructure(tournamentId);

            Assert.That(structure.TournamentId, Is.EqualTo(tournamentId));
            Assert.That(structure.Format, Is.EqualTo(TournamentFormat.SingleElimination));
            Assert.That(structure.IsTeamTournament, Is.False);
            Assert.That(structure.Stages.Count, Is.EqualTo(1));

            var stage = structure.Stages.Single();
            Assert.That(stage.Type, Is.EqualTo(StageType.SingleEliminationBracket));
            Assert.That(stage.Rounds, Is.Not.Null);
            Assert.That(stage.Rounds!.Count, Is.EqualTo(3), "8 players -> 3 rounds");

            int totalMatches = stage.Rounds!.Sum(r => r.Matches.Count);
            Assert.That(totalMatches, Is.EqualTo(7), "N-1 matches surfaced in the structure");
        }

        [Test]
        public async Task GetTournamentStructureV2_AsAdmin_CanManageIsTrue()
        {
            var harness = new BracketTestHarness();
            var tournamentId = await harness.SeedSoloTournamentAsync(TournamentFormat.SingleElimination, 8);
            await harness.NewService().GenerateSingleEliminationBracket(tournamentId);

            var structure = await harness.NewService().GetTournamentStructureV2(tournamentId);

            Assert.That(structure.CanManage, Is.True, "admin/owner can manage");
            Assert.That(structure.Stages.Count, Is.EqualTo(1));
        }

        [Test]
        public async Task GetTournamentStructure_League_ReturnsGroupWithStandings()
        {
            var harness = new BracketTestHarness();
            var tournamentId = await harness.SeedSoloTournamentAsync(TournamentFormat.League, 6);
            await harness.NewService().GenerateLeagueTournament(tournamentId);

            var structure = await harness.NewService().GetTournamentStructure(tournamentId);

            var stage = structure.Stages.Single();
            Assert.That(stage.Type, Is.EqualTo(StageType.League));
            Assert.That(stage.Groups, Is.Not.Null);
            Assert.That(stage.Groups!.Count, Is.EqualTo(1), "one league table");
        }

        [Test]
        public async Task GetTournamentStructure_DoubleElimination_ReturnsWinnersAndLosersStages()
        {
            var harness = new BracketTestHarness();
            var tournamentId = await harness.SeedSoloTournamentAsync(TournamentFormat.DoubleElimination, 8);
            await harness.NewService().GenerateDoubleEliminationBracket(tournamentId);

            var structure = await harness.NewService().GetTournamentStructure(tournamentId);

            var types = structure.Stages.Select(s => s.Type).ToList();
            Assert.That(types, Does.Contain(StageType.DoubleEliminationWinnersBracket));
            Assert.That(types, Does.Contain(StageType.DoubleEliminationLosersBracket));
        }

        [Test]
        public async Task GetTournamentStructure_UnknownTournament_Throws()
        {
            var harness = new BracketTestHarness();
            Assert.That(async () => await harness.NewService().GetTournamentStructure(System.Guid.NewGuid()),
                Throws.Exception);
        }
    }
}
