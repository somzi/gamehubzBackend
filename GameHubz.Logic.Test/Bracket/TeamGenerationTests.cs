using System.Linq;
using System.Threading.Tasks;

using NUnit.Framework;

using GameHubz.DataModels.Enums;

namespace GameHubz.Logic.Test.Bracket
{
    [TestFixture]
    internal sealed class TeamGenerationTests
    {
        // ---- Team single elimination --------------------------------------

        [TestCase(4, 2)]
        [TestCase(8, 3)]
        [TestCase(16, 4)]
        public async Task TeamSingleElim_PowerOfTwo_HasNMinusOneTeamMatches(int teams, int expectedRounds)
        {
            var harness = new BracketTestHarness();
            var tournamentId = await harness.SeedTeamTournamentAsync(TournamentFormat.SingleElimination, teams, teamSize: 5);

            await harness.Service.GenerateTeamSingleEliminationBracket(tournamentId);

            var teamMatches = harness.TeamMatches(tournamentId);
            Assert.That(teamMatches.Count, Is.EqualTo(teams - 1), "N-1 team matches");
            Assert.That(teamMatches.Select(m => m.RoundNumber).Max(), Is.EqualTo(expectedRounds), "round count");
            Assert.That(teamMatches.Count(m => m.RoundNumber == 1), Is.EqualTo(teams / 2), "first round team match count");
        }

        [Test]
        public async Task TeamSingleElim_BuildsTeamSizeSubMatchesPerLiveFixture()
        {
            const int teams = 8;
            const int teamSize = 5;

            var harness = new BracketTestHarness();
            var tournamentId = await harness.SeedTeamTournamentAsync(TournamentFormat.SingleElimination, teams, teamSize);

            await harness.Service.GenerateTeamSingleEliminationBracket(tournamentId);

            // Only round-1 fixtures have both teams at generation -> teams/2 fixtures, each with
            // teamSize sub-matches. Later rounds are empty until results come in.
            var subMatches = harness.Matches(tournamentId);
            Assert.That(subMatches.Count, Is.EqualTo((teams / 2) * teamSize), "sub-matches = live fixtures x teamSize");
            Assert.That(subMatches.All(m => m.TeamMatchId.HasValue), Is.True, "every sub-match belongs to a team match");
        }

        [Test]
        public async Task TeamSingleElim_NonPowerOfTwo_AutoAdvancesTeamByes()
        {
            const int teams = 6;
            const int teamSize = 3;
            int bracketSize = BracketTestHarness.NextPowerOfTwo(teams); // 8

            var harness = new BracketTestHarness();
            var tournamentId = await harness.SeedTeamTournamentAsync(TournamentFormat.SingleElimination, teams, teamSize);

            await harness.Service.GenerateTeamSingleEliminationBracket(tournamentId);

            var teamMatches = harness.TeamMatches(tournamentId);
            Assert.That(teamMatches.Count, Is.EqualTo(bracketSize - 1), "tree size bracketSize-1");

            int byes = teamMatches.Count(m => m.Status == TeamMatchStatus.Completed);
            Assert.That(byes, Is.EqualTo(bracketSize - teams), "one bye per surplus slot");
        }

        [Test]
        public async Task TeamSingleElim_MissingTeamSize_Throws()
        {
            var harness = new BracketTestHarness();
            // Seed as solo (no TeamSize) but call the team generator.
            var tournamentId = await harness.SeedSoloTournamentAsync(TournamentFormat.SingleElimination, 8);

            Assert.That(async () => await harness.Service.GenerateTeamSingleEliminationBracket(tournamentId),
                Throws.Exception);
        }

        // ---- Team double elimination --------------------------------------

        [TestCase(4)]
        [TestCase(8)]
        public async Task TeamDoubleElim_PowerOfTwo_HasWinnersLosersAndGrandFinal(int teams)
        {
            var harness = new BracketTestHarness();
            var tournamentId = await harness.SeedTeamTournamentAsync(TournamentFormat.DoubleElimination, teams, teamSize: 4);

            await harness.Service.GenerateTeamDoubleEliminationBracket(tournamentId);

            var teamMatches = harness.TeamMatches(tournamentId);

            int grandFinals = teamMatches.Count(m => m.IsGrandFinal);
            int losersBracket = teamMatches.Count(m => !m.IsUpperBracket);
            int winnersBracket = teamMatches.Count(m => m.IsUpperBracket && !m.IsGrandFinal);

            Assert.That(grandFinals, Is.EqualTo(1), "exactly one grand final");
            Assert.That(winnersBracket, Is.EqualTo(teams - 1), "winners bracket N-1");
            Assert.That(losersBracket, Is.EqualTo(teams - 2), "losers bracket N-2");
            Assert.That(teamMatches.Count, Is.EqualTo(2 * teams - 2), "total 2N-2");

            var stageTypes = harness.Stages(tournamentId).Select(s => s.Type).ToList();
            Assert.That(stageTypes, Does.Contain(StageType.DoubleEliminationWinnersBracket));
            Assert.That(stageTypes, Does.Contain(StageType.DoubleEliminationLosersBracket));
        }

        // ---- Team league ---------------------------------------------------

        [TestCase(4, 3)]
        [TestCase(5, 2)]
        public async Task TeamLeague_HasAllPairingsAndSubMatches(int teams, int teamSize)
        {
            int expectedTeamMatches = teams * (teams - 1) / 2;

            var harness = new BracketTestHarness();
            var tournamentId = await harness.SeedTeamTournamentAsync(TournamentFormat.League, teams, teamSize);

            await harness.Service.GenerateTeamLeagueTournament(tournamentId);

            var teamMatches = harness.TeamMatches(tournamentId);
            Assert.That(teamMatches.Count, Is.EqualTo(expectedTeamMatches), "C(N,2) team fixtures");

            var subMatches = harness.Matches(tournamentId);
            Assert.That(subMatches.Count, Is.EqualTo(expectedTeamMatches * teamSize),
                "every league fixture has teamSize sub-matches");
        }

        // ---- Team group stage ---------------------------------------------

        [Test]
        public async Task TeamGroupStage_BuildsGroupRoundRobinsAndKnockoutStage()
        {
            const int teamSize = 2;

            var harness = new BracketTestHarness();
            var tournamentId = await harness.SeedTeamTournamentAsync(TournamentFormat.GroupStageWithKnockout, 8, teamSize);

            await harness.Service.GenerateTeamGroupStageWithKnockout(tournamentId, numberOfGroups: 2, qualifiersPerGroup: 2);

            var teamMatches = harness.TeamMatches(tournamentId);
            Assert.That(teamMatches.Count, Is.EqualTo(12), "2 groups x C(4,2)=6 team fixtures");

            var subMatches = harness.Matches(tournamentId);
            Assert.That(subMatches.Count, Is.EqualTo(12 * teamSize), "sub-matches for every group fixture");

            var stageTypes = harness.Stages(tournamentId).Select(s => s.Type).ToList();
            Assert.That(stageTypes, Does.Contain(StageType.GroupStage));
            Assert.That(stageTypes, Does.Contain(StageType.SingleEliminationBracket));
        }
    }
}
