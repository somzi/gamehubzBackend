using System;
using System.Linq;
using System.Threading.Tasks;

using NUnit.Framework;

using GameHubz.DataModels.Domain;
using GameHubz.DataModels.Enums;
using GameHubz.DataModels.Models;
using GameHubz.Logic.Exceptions;

namespace GameHubz.Logic.Test.Bracket
{
    // The validation ring around UpdateMatchResult / RejectProposedResult: round gating, wrong-id
    // and TBD-slot rejection, and the approval-mode authorization edges the ApprovalTests happy
    // paths don't touch. Every guard must surface as BusinessRuleException (HTTP 400 with the real
    // message), never a bare 500. SQLite harness (advancement path).
    [TestFixture]
    internal sealed class UpdateMatchResultGuardTests
    {
        [Test]
        public async Task RoundNotOpenYet_CannotBeReported()
        {
            var harness = new BracketTestHarness(useSqlite: true);
            var tournamentId = await harness.SeedSoloTournamentAsync(TournamentFormat.SingleElimination, 4);
            await harness.NewService().GenerateSingleEliminationBracket(tournamentId);

            var match = harness.Matches(tournamentId).First(m => m.RoundNumber == 1);

            using (var ctx = harness.ReadContext())
            {
                var tracked = ctx.Set<MatchEntity>().Single(m => m.Id == match.Id);
                tracked.RoundOpenAt = DateTime.UtcNow.AddHours(1);
                await ctx.SaveChangesAsync();
            }

            Assert.That(async () => await harness.NewService().UpdateMatchResult(new MatchResultDto
            {
                MatchId = match.Id!.Value, TournamentId = tournamentId, HomeScore = 2, AwayScore = 0,
            }), Throws.TypeOf<BusinessRuleException>().With.Message.Contains("not open"));
        }

        [Test]
        public async Task MatchFromAnotherTournament_IsRejected()
        {
            var harness = new BracketTestHarness(useSqlite: true);
            var tournamentA = await harness.SeedSoloTournamentAsync(TournamentFormat.SingleElimination, 4);
            var tournamentB = await harness.SeedSoloTournamentAsync(TournamentFormat.SingleElimination, 4);
            await harness.NewService().GenerateSingleEliminationBracket(tournamentA);
            await harness.NewService().GenerateSingleEliminationBracket(tournamentB);

            var matchFromA = harness.Matches(tournamentA).First(m => m.RoundNumber == 1);

            Assert.That(async () => await harness.NewService().UpdateMatchResult(new MatchResultDto
            {
                MatchId = matchFromA.Id!.Value, TournamentId = tournamentB, HomeScore = 2, AwayScore = 0,
            }), Throws.TypeOf<BusinessRuleException>(), "tournament id must match the match's tournament");
        }

        [Test]
        public async Task TbdSlot_CannotBeReported()
        {
            var harness = new BracketTestHarness(useSqlite: true);
            var tournamentId = await harness.SeedSoloTournamentAsync(TournamentFormat.SingleElimination, 4);
            await harness.NewService().GenerateSingleEliminationBracket(tournamentId);

            // The final has no participants until both semi-finals are played.
            var final = harness.Matches(tournamentId).Single(m => m.Stage == MatchStage.Final);
            Assert.That(final.HomeParticipantId, Is.Null, "precondition: final still TBD");

            Assert.That(async () => await harness.NewService().UpdateMatchResult(new MatchResultDto
            {
                MatchId = final.Id!.Value, TournamentId = tournamentId, HomeScore = 2, AwayScore = 0,
            }), Throws.TypeOf<BusinessRuleException>(), "a slot awaiting feeders takes no result");
        }

        [Test]
        public async Task ApprovalMode_NonParticipant_CannotPropose()
        {
            var harness = new BracketTestHarness(useSqlite: true);
            var tournamentId = await harness.SeedSoloTournamentAsync(
                TournamentFormat.SingleElimination, 4, requireResultApproval: true);
            await harness.NewService().GenerateSingleEliminationBracket(tournamentId);

            var match = harness.Matches(tournamentId).First(m => m.RoundNumber == 1);
            var outsiderId = Guid.NewGuid();
            await harness.DenyManageFor(outsiderId, tournamentId);

            Assert.That(async () => await harness.NewServiceAsUser(outsiderId).UpdateMatchResult(new MatchResultDto
            {
                MatchId = match.Id!.Value, TournamentId = tournamentId, HomeScore = 2, AwayScore = 0,
            }), Throws.TypeOf<BusinessRuleException>().With.Message.Contains("participant"));
        }

        [Test]
        public async Task ApprovalMode_ParticipantCannotEditConfirmedResult()
        {
            var harness = new BracketTestHarness(useSqlite: true);
            var tournamentId = await harness.SeedSoloTournamentAsync(
                TournamentFormat.SingleElimination, 4, requireResultApproval: true);
            await harness.NewService().GenerateSingleEliminationBracket(tournamentId);

            var match = harness.Matches(tournamentId).First(m => m.RoundNumber == 1);
            var participantUserId = harness.ParticipantUserId(match.HomeParticipantId!.Value);
            await harness.DenyManageFor(participantUserId, tournamentId);

            await harness.NewServiceAsUser(participantUserId).UpdateMatchResult(new MatchResultDto
            {
                MatchId = match.Id!.Value, TournamentId = tournamentId, HomeScore = 2, AwayScore = 0,
            });
            await harness.NewService().ApproveProposedResult(match.Id!.Value);

            // Once confirmed, the participant edit path is closed — only a manager may amend.
            Assert.That(async () => await harness.NewServiceAsUser(participantUserId).UpdateMatchResult(new MatchResultDto
            {
                MatchId = match.Id!.Value, TournamentId = tournamentId, HomeScore = 0, AwayScore = 2,
            }), Throws.TypeOf<BusinessRuleException>().With.Message.Contains("final"));
        }

        [Test]
        public async Task ApprovalMode_ManagerReportsDirectly_NoProposalStep()
        {
            var harness = new BracketTestHarness(useSqlite: true);
            var tournamentId = await harness.SeedSoloTournamentAsync(
                TournamentFormat.SingleElimination, 4, requireResultApproval: true);
            await harness.NewService().GenerateSingleEliminationBracket(tournamentId);

            var match = harness.Matches(tournamentId).First(m => m.RoundNumber == 1);

            // Default harness token is a manager — the approval gate applies to participants only.
            await harness.NewService().UpdateMatchResult(new MatchResultDto
            {
                MatchId = match.Id!.Value, TournamentId = tournamentId, HomeScore = 2, AwayScore = 0,
            });

            var reported = harness.Match(match.Id!.Value);
            Assert.That(reported.Status, Is.EqualTo(MatchStatus.Completed), "manager report is immediately official");
            Assert.That(reported.ProposedByUserId, Is.Null, "no proposal was recorded");
        }

        [Test]
        public async Task RejectingYourOwnProposal_Throws()
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

            Assert.That(async () => await harness.NewServiceAsUser(proposerUserId).RejectProposedResult(match.Id!.Value),
                Throws.TypeOf<BusinessRuleException>().With.Message.Contains("own"),
                "the proposer must submit a corrected result, not reject their own proposal");
        }
    }
}
