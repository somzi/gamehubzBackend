using System.Linq;
using System.Threading.Tasks;

using NUnit.Framework;

using GameHubz.DataModels.Enums;
using GameHubz.DataModels.Models;
using GameHubz.Logic.Exceptions;

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

        // The opponent's screen was opened before the first report landed, so it still offers the
        // entry form. Their own report used to replace the waiting one and swap who has to confirm:
        // both players then saw "waiting for the opponent" and neither could confirm anything.
        [Test]
        public async Task OpponentReport_OverAWaitingProposal_IsRefused_AndTheProposalStands()
        {
            var harness = new BracketTestHarness(useSqlite: true);
            var tournamentId = await harness.SeedSoloTournamentAsync(
                TournamentFormat.SingleElimination, 4, requireResultApproval: true);
            await harness.NewService().GenerateSingleEliminationBracket(tournamentId);

            var match = harness.Matches(tournamentId).First(m => m.RoundNumber == 1);
            var proposerUserId = harness.ParticipantUserId(match.HomeParticipantId!.Value);
            var opponentUserId = harness.ParticipantUserId(match.AwayParticipantId!.Value);
            await harness.DenyManageFor(proposerUserId, tournamentId);
            await harness.DenyManageFor(opponentUserId, tournamentId);

            await harness.NewServiceAsUser(proposerUserId).UpdateMatchResult(new MatchResultDto
            {
                MatchId = match.Id!.Value, TournamentId = tournamentId, HomeScore = 2, AwayScore = 0,
            });

            Assert.ThrowsAsync<BusinessRuleException>(() =>
                harness.NewServiceAsUser(opponentUserId).UpdateMatchResult(new MatchResultDto
                {
                    MatchId = match.Id!.Value, TournamentId = tournamentId, HomeScore = 0, AwayScore = 2,
                }));

            var after = harness.Match(match.Id!.Value);
            Assert.That(after.ProposedByUserId, Is.EqualTo(proposerUserId), "still the first reporter's");
            Assert.That(after.ProposedHomeScore, Is.EqualTo(2));
            Assert.That(after.ProposedAwayScore, Is.EqualTo(0));

            // The opponent is not stuck: confirming is what the refusal sends them to.
            await harness.NewServiceAsUser(opponentUserId).ApproveProposedResult(match.Id!.Value);
            Assert.That(harness.Match(match.Id!.Value).Status, Is.EqualTo(MatchStatus.Completed));
        }

        [Test]
        public async Task Proposer_CanAmendTheirOwnWaitingProposal()
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
            await harness.NewServiceAsUser(proposerUserId).UpdateMatchResult(new MatchResultDto
            {
                MatchId = match.Id!.Value, TournamentId = tournamentId, HomeScore = 3, AwayScore = 1,
            });

            var amended = harness.Match(match.Id!.Value);
            Assert.That(amended.ProposedByUserId, Is.EqualTo(proposerUserId));
            Assert.That(amended.ProposedHomeScore, Is.EqualTo(3));
            Assert.That(amended.ProposedAwayScore, Is.EqualTo(1));
        }

        [Test]
        public async Task AfterARejection_TheOpponentCanReportTheirOwnResult()
        {
            var harness = new BracketTestHarness(useSqlite: true);
            var tournamentId = await harness.SeedSoloTournamentAsync(
                TournamentFormat.SingleElimination, 4, requireResultApproval: true);
            await harness.NewService().GenerateSingleEliminationBracket(tournamentId);

            var match = harness.Matches(tournamentId).First(m => m.RoundNumber == 1);
            var proposerUserId = harness.ParticipantUserId(match.HomeParticipantId!.Value);
            var opponentUserId = harness.ParticipantUserId(match.AwayParticipantId!.Value);
            await harness.DenyManageFor(proposerUserId, tournamentId);
            await harness.DenyManageFor(opponentUserId, tournamentId);

            await harness.NewServiceAsUser(proposerUserId).UpdateMatchResult(new MatchResultDto
            {
                MatchId = match.Id!.Value, TournamentId = tournamentId, HomeScore = 2, AwayScore = 0,
            });
            await harness.NewServiceAsUser(opponentUserId).RejectProposedResult(match.Id!.Value);
            await harness.NewServiceAsUser(opponentUserId).UpdateMatchResult(new MatchResultDto
            {
                MatchId = match.Id!.Value, TournamentId = tournamentId, HomeScore = 0, AwayScore = 2,
            });

            var counter = harness.Match(match.Id!.Value);
            Assert.That(counter.ProposedByUserId, Is.EqualTo(opponentUserId));
            Assert.That(counter.ProposedAwayScore, Is.EqualTo(2));
        }
    }
}
