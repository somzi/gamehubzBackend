using System.Linq;
using System.Threading.Tasks;

using NUnit.Framework;

using GameHubz.DataModels.Enums;
using GameHubz.DataModels.Models;

namespace GameHubz.Logic.Test.Bracket
{
    // Result-approval flow. In approval mode a participant's report becomes a pending proposal that a
    // manager (or the opponent) must confirm before the bracket advances. Runs on SQLite (advancement path).
    [TestFixture]
    internal sealed class ApprovalTests
    {
        [Test]
        public async Task ParticipantReport_BecomesPendingProposal_NotACompletedResult()
        {
            var harness = new BracketTestHarness(useSqlite: true);
            var tournamentId = await harness.SeedSoloTournamentAsync(
                TournamentFormat.SingleElimination, 4, requireResultApproval: true);
            await harness.NewService().GenerateSingleEliminationBracket(tournamentId);

            var match = harness.Matches(tournamentId).First(m => m.RoundNumber == 1);
            var proposerUserId = harness.ParticipantUserId(match.HomeParticipantId!.Value);
            await harness.DenyManageFor(proposerUserId, tournamentId);

            await harness.NewServiceAsUser(proposerUserId).UpdateMatchResult(new MatchResultDto
            {
                MatchId = match.Id!.Value, TournamentId = tournamentId, HomeScore = 2, AwayScore = 0,
            });

            var afterPropose = harness.Match(match.Id!.Value);
            Assert.That(afterPropose.Status, Is.Not.EqualTo(MatchStatus.Completed), "not yet official");
            Assert.That(afterPropose.ProposedByUserId, Is.EqualTo(proposerUserId), "proposal recorded");
            Assert.That(afterPropose.ProposedHomeScore, Is.EqualTo(2));
            Assert.That(afterPropose.ProposedAwayScore, Is.EqualTo(0));
            Assert.That(afterPropose.WinnerParticipantId, Is.Null, "no winner until approved");
        }

        [Test]
        public async Task ManagerApproval_CommitsProposalAndAdvances()
        {
            var harness = new BracketTestHarness(useSqlite: true);
            var tournamentId = await harness.SeedSoloTournamentAsync(
                TournamentFormat.SingleElimination, 4, requireResultApproval: true);
            await harness.NewService().GenerateSingleEliminationBracket(tournamentId);

            var match = harness.Matches(tournamentId).First(m => m.RoundNumber == 1);
            var proposerUserId = harness.ParticipantUserId(match.HomeParticipantId!.Value);
            await harness.DenyManageFor(proposerUserId, tournamentId);

            await harness.NewServiceAsUser(proposerUserId).UpdateMatchResult(new MatchResultDto
            {
                MatchId = match.Id!.Value, TournamentId = tournamentId, HomeScore = 2, AwayScore = 0,
            });

            // Admin (default token) approves.
            await harness.NewService().ApproveProposedResult(match.Id!.Value);

            var approved = harness.Match(match.Id!.Value);
            Assert.That(approved.Status, Is.EqualTo(MatchStatus.Completed), "now official");
            Assert.That(approved.WinnerParticipantId, Is.EqualTo(match.HomeParticipantId), "proposed winner committed");
            Assert.That(approved.HomeUserScore, Is.EqualTo(2));
            Assert.That(approved.ProposedByUserId, Is.Null, "proposal cleared");
        }

        [Test]
        public async Task ManagerRejection_ClearsProposalAndLeavesMatchOpen()
        {
            var harness = new BracketTestHarness(useSqlite: true);
            var tournamentId = await harness.SeedSoloTournamentAsync(
                TournamentFormat.SingleElimination, 4, requireResultApproval: true);
            await harness.NewService().GenerateSingleEliminationBracket(tournamentId);

            var match = harness.Matches(tournamentId).First(m => m.RoundNumber == 1);
            var proposerUserId = harness.ParticipantUserId(match.HomeParticipantId!.Value);
            await harness.DenyManageFor(proposerUserId, tournamentId);

            await harness.NewServiceAsUser(proposerUserId).UpdateMatchResult(new MatchResultDto
            {
                MatchId = match.Id!.Value, TournamentId = tournamentId, HomeScore = 2, AwayScore = 0,
            });

            await harness.NewService().RejectProposedResult(match.Id!.Value);

            var rejected = harness.Match(match.Id!.Value);
            Assert.That(rejected.Status, Is.Not.EqualTo(MatchStatus.Completed), "still open");
            Assert.That(rejected.ProposedByUserId, Is.Null, "proposal cleared");
            Assert.That(rejected.ProposedHomeScore, Is.Null);
        }
    }
}
