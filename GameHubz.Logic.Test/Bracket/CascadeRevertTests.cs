using System.Linq;
using System.Threading.Tasks;

using NUnit.Framework;

using GameHubz.DataModels.Enums;
using GameHubz.DataModels.Models;

namespace GameHubz.Logic.Test.Bracket
{
    // Cascade revert/edit: an owner/admin reopening a result whose downstream matches were already
    // played. Runs on the SQLite harness (the advancement path takes the Postgres advisory lock).
    // The default harness token is an Admin, so CanManageTournamentAsync short-circuits to true —
    // i.e. these act as a privileged caller, which is what the cascade requires.
    [TestFixture]
    internal sealed class CascadeRevertTests
    {
        // The user's exact scenario: a Winners-Bracket match whose loser has already played its
        // Losers-Bracket match. A plain revert is locked; a cascade revert reopens the LB match first,
        // then the WB match.
        [Test]
        public async Task CascadeRevert_DoubleElim_ReopensLoserBracketThenWinnersMatch()
        {
            var harness = new BracketTestHarness(useSqlite: true);
            var tournamentId = await harness.SeedSoloTournamentAsync(TournamentFormat.DoubleElimination, 4);
            await harness.NewService().GenerateDoubleEliminationBracket(tournamentId);

            var wbR1 = harness.Matches(tournamentId)
                .Where(m => m.IsUpperBracket && m.Stage != MatchStage.GrandFinal && m.RoundNumber == 1)
                .OrderBy(m => m.MatchOrder)
                .ToList();
            Assert.That(wbR1.Count, Is.EqualTo(2), "4-player DE has two winners round-1 matches");

            // Play both WB round-1 matches — both losers drop into the single LB round-1 match.
            foreach (var m in wbR1)
            {
                await harness.NewService().UpdateMatchResult(new MatchResultDto
                {
                    MatchId = m.Id!.Value, TournamentId = tournamentId, HomeScore = 2, AwayScore = 1,
                });
            }

            var target = wbR1[0];
            var lbId = harness.Match(target.Id!.Value).NextMatchLoserBracketId!.Value;

            // Play the LB match — now the target's loser has progressed.
            await harness.NewService().UpdateMatchResult(new MatchResultDto
            {
                MatchId = lbId, TournamentId = tournamentId, HomeScore = 2, AwayScore = 0,
            });
            Assert.That(harness.Match(lbId).Status, Is.EqualTo(MatchStatus.Completed), "LB match played");

            // Plain revert is locked because the loser-bracket match progressed.
            Assert.That(async () => await harness.NewService().RevertMatchResult(target.Id!.Value),
                Throws.Exception, "revert is locked without cascade");

            // Cascade revert reopens the LB match first, then the WB match.
            await harness.NewService().RevertMatchResult(target.Id!.Value, cascade: true);

            var revertedWb = harness.Match(target.Id!.Value);
            Assert.That(revertedWb.Status, Is.EqualTo(MatchStatus.Scheduled), "WB match reopened");
            Assert.That(revertedWb.WinnerParticipantId, Is.Null, "WB winner cleared");

            var revertedLb = harness.Match(lbId);
            Assert.That(revertedLb.Status, Is.Not.EqualTo(MatchStatus.Completed), "LB match reopened");
            Assert.That(revertedLb.WinnerParticipantId, Is.Null, "LB winner cleared");
        }

        // A same-winner score correction is applied in place even when the bracket has progressed —
        // the winner (and everything below it) is unchanged, so there is nothing to revert.
        [Test]
        public async Task EditSameWinner_AppliedInPlace_EvenWhenDownstreamProgressed()
        {
            var harness = new BracketTestHarness(useSqlite: true);
            var tournamentId = await harness.SeedSoloTournamentAsync(TournamentFormat.SingleElimination, 4);
            await harness.NewService().GenerateSingleEliminationBracket(tournamentId);

            var semis = harness.Matches(tournamentId)
                .Where(m => m.RoundNumber == 1)
                .OrderBy(m => m.MatchOrder)
                .ToList();
            foreach (var semi in semis)
            {
                await harness.NewService().UpdateMatchResult(new MatchResultDto
                {
                    MatchId = semi.Id!.Value, TournamentId = tournamentId, HomeScore = 2, AwayScore = 1,
                });
            }

            var finalId = semis[0].NextMatchId!.Value;
            await harness.NewService().UpdateMatchResult(new MatchResultDto
            {
                MatchId = finalId, TournamentId = tournamentId, HomeScore = 1, AwayScore = 0,
            });

            var originalWinner = harness.Match(semis[0].Id!.Value).WinnerParticipantId;

            // Final has progressed, yet a same-winner re-score of the semi-final goes straight through.
            await harness.NewService().UpdateMatchResult(new MatchResultDto
            {
                MatchId = semis[0].Id!.Value, TournamentId = tournamentId, HomeScore = 5, AwayScore = 2,
            });

            var edited = harness.Match(semis[0].Id!.Value);
            Assert.That(edited.Status, Is.EqualTo(MatchStatus.Completed), "stays completed");
            Assert.That(edited.HomeUserScore, Is.EqualTo(5), "score updated");
            Assert.That(edited.AwayUserScore, Is.EqualTo(2), "score updated");
            Assert.That(edited.WinnerParticipantId, Is.EqualTo(originalWinner), "winner unchanged");
            Assert.That(harness.Match(finalId).Status, Is.EqualTo(MatchStatus.Completed), "final untouched");
        }

        // A winner-changing edit with cascade reopens the downstream match and re-advances the new winner.
        [Test]
        public async Task CascadeEdit_FlipsWinner_AndReopensDownstream()
        {
            var harness = new BracketTestHarness(useSqlite: true);
            var tournamentId = await harness.SeedSoloTournamentAsync(TournamentFormat.SingleElimination, 4);
            await harness.NewService().GenerateSingleEliminationBracket(tournamentId);

            var semis = harness.Matches(tournamentId)
                .Where(m => m.RoundNumber == 1)
                .OrderBy(m => m.MatchOrder)
                .ToList();
            foreach (var semi in semis)
            {
                await harness.NewService().UpdateMatchResult(new MatchResultDto
                {
                    MatchId = semi.Id!.Value, TournamentId = tournamentId, HomeScore = 2, AwayScore = 1,
                });
            }

            var finalId = semis[0].NextMatchId!.Value;
            await harness.NewService().UpdateMatchResult(new MatchResultDto
            {
                MatchId = finalId, TournamentId = tournamentId, HomeScore = 1, AwayScore = 0,
            });

            var semi0Id = semis[0].Id!.Value;
            var awayParticipant = harness.Match(semi0Id).AwayParticipantId;

            // Without cascade this is locked (final progressed); with cascade it reopens the final.
            Assert.That(async () => await harness.NewService().UpdateMatchResult(new MatchResultDto
            {
                MatchId = semi0Id, TournamentId = tournamentId, HomeScore = 0, AwayScore = 3,
            }), Throws.Exception, "winner-changing edit is locked without cascade");

            await harness.NewService().UpdateMatchResult(new MatchResultDto
            {
                MatchId = semi0Id, TournamentId = tournamentId, HomeScore = 0, AwayScore = 3, Cascade = true,
            });

            var edited = harness.Match(semi0Id);
            Assert.That(edited.WinnerParticipantId, Is.EqualTo(awayParticipant), "winner flipped to the away side");

            var final = harness.Match(finalId);
            Assert.That(final.Status, Is.Not.EqualTo(MatchStatus.Completed), "final reopened");
            bool newWinnerInFinal = final.HomeParticipantId == awayParticipant || final.AwayParticipantId == awayParticipant;
            Assert.That(newWinnerInFinal, Is.True, "new winner advanced into the final");
        }
    }
}
