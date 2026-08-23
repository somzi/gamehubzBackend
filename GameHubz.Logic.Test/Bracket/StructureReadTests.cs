using System.Linq;
using System.Threading.Tasks;

using NUnit.Framework;

using GameHubz.DataModels.Domain;
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
        public async Task TeamCard_ReportsTheFormatOfTheGamesStillToBePlayed()
        {
            var harness = new BracketTestHarness();
            var tid = await harness.SeedTeamTournamentAsync(
                TournamentFormat.SingleElimination, teamCount: 4, teamSize: 2, bestOf: 3);
            await harness.Service.GenerateTeamSingleEliminationBracket(tid);

            var tie = harness.TeamMatches(tid).First(tm => tm.RoundNumber == 1);

            // One game of the tie is played, freezing its Bo3. The organizer then moves the round
            // to Bo5, which re-formats only the sub-matches that are still open — so the tie now
            // holds two formats at once, and the card has to report the one still to be played.
            using (var ctx = harness.ReadContext())
            {
                var subs = ctx.Set<MatchEntity>()
                    .Where(m => m.TeamMatchId == tie.Id)
                    .OrderBy(m => m.MatchOrder)
                    .ToList();

                subs[0].BestOf = 3;
                subs[0].GamesJson = """[{"HomeScore":1,"AwayScore":0,"SeriesNumber":1}]""";
                foreach (var open in subs.Skip(1))
                {
                    open.BestOf = 5;
                    open.TiebreakBestOf = 1;
                }

                await ctx.SaveChangesAsync();
            }

            var structure = await harness.NewService().GetTournamentStructure(tid);

            var card = structure.Stages
                .Single().Rounds!
                .Single(r => r.RoundNumber == 1).Matches
                .Single(m => m.TeamMatchId == tie.Id);

            Assert.Multiple(() =>
            {
                Assert.That(card.BestOf, Is.EqualTo(5), "the card names the format of the games still to come");
                Assert.That(card.TiebreakBestOf, Is.EqualTo(1), "and takes the tiebreak from the same sub-match");
            });
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
