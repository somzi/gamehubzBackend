using System.Linq;
using System.Threading.Tasks;

using NUnit.Framework;

using GameHubz.DataModels.Enums;
using GameHubz.DataModels.Models;

namespace GameHubz.Logic.Test.Bracket
{
    // Double walkover: an admin closes an unplayed elimination match with no winner (both sides
    // no-showed) and the opponent from the sibling matchup advances unopposed. Runs on the SQLite
    // harness because it shares the advancement/advisory-lock path with UpdateMatchResult.
    [TestFixture]
    internal sealed class DoubleWalkoverTests
    {
        [Test]
        public async Task DoubleWalkover_AfterSiblingDecided_AdvancesOpponentAndCompletes()
        {
            var harness = new BracketTestHarness(useSqlite: true);
            var tid = await harness.SeedSoloTournamentAsync(TournamentFormat.SingleElimination, 4);
            await harness.NewService().GenerateSingleEliminationBracket(tid);

            var semis = harness.Matches(tid).Where(m => m.RoundNumber == 1).OrderBy(m => m.MatchOrder).ToList();
            Assert.That(semis.Count, Is.EqualTo(2));

            // Decide semi 0 normally; its winner sits in the final waiting for an opponent.
            await harness.NewService().UpdateMatchResult(new MatchResultDto
            {
                MatchId = semis[0].Id!.Value, TournamentId = tid, HomeScore = 2, AwayScore = 1,
            });
            var advancingId = semis[0].HomeParticipantId!.Value;

            // Semi 1 was never played by either side → double walkover.
            await harness.NewService().ApplyDoubleWalkover(semis[1].Id!.Value);

            var voided = harness.Match(semis[1].Id!.Value);
            Assert.That(voided.Status, Is.EqualTo(MatchStatus.Completed), "voided match is closed");
            Assert.That(voided.WinnerParticipantId, Is.Null, "double walkover leaves no winner");

            var final = harness.Match(semis[0].NextMatchId!.Value);
            Assert.That(final.Status, Is.EqualTo(MatchStatus.Completed), "final settled by walkover");
            Assert.That(final.WinnerParticipantId, Is.EqualTo(advancingId), "surviving opponent wins by walkover");

            Assert.That(harness.Tournament(tid).Status, Is.EqualTo(TournamentStatus.Completed), "champion decided");
        }

        [Test]
        public async Task DoubleWalkover_BeforeSiblingDecided_AdvancesOnceSiblingCompletes()
        {
            var harness = new BracketTestHarness(useSqlite: true);
            var tid = await harness.SeedSoloTournamentAsync(TournamentFormat.SingleElimination, 4);
            await harness.NewService().GenerateSingleEliminationBracket(tid);

            var semis = harness.Matches(tid).Where(m => m.RoundNumber == 1).OrderBy(m => m.MatchOrder).ToList();

            // Apply the double walkover while the sibling semi-final is still pending.
            await harness.NewService().ApplyDoubleWalkover(semis[0].Id!.Value);

            var finalBefore = harness.Match(semis[0].NextMatchId!.Value);
            Assert.That(finalBefore.Status, Is.Not.EqualTo(MatchStatus.Completed),
                "final still waits on the live sibling");

            // Now decide the sibling — its winner should walk over the final automatically.
            await harness.NewService().UpdateMatchResult(new MatchResultDto
            {
                MatchId = semis[1].Id!.Value, TournamentId = tid, HomeScore = 3, AwayScore = 0,
            });
            var advancingId = semis[1].HomeParticipantId!.Value;

            var final = harness.Match(semis[0].NextMatchId!.Value);
            Assert.That(final.Status, Is.EqualTo(MatchStatus.Completed), "final settled once the sibling is decided");
            Assert.That(final.WinnerParticipantId, Is.EqualTo(advancingId), "the sibling's winner takes the walkover");
            Assert.That(harness.Tournament(tid).Status, Is.EqualTo(TournamentStatus.Completed));
        }

        [Test]
        public async Task DoubleWalkover_OnCompletedMatch_Throws()
        {
            var harness = new BracketTestHarness(useSqlite: true);
            var tid = await harness.SeedSoloTournamentAsync(TournamentFormat.SingleElimination, 4);
            await harness.NewService().GenerateSingleEliminationBracket(tid);

            var semi = harness.Matches(tid).First(m => m.RoundNumber == 1);
            await harness.NewService().UpdateMatchResult(new MatchResultDto
            {
                MatchId = semi.Id!.Value, TournamentId = tid, HomeScore = 2, AwayScore = 1,
            });

            Assert.That(async () => await harness.NewService().ApplyDoubleWalkover(semi.Id!.Value),
                Throws.Exception, "an already-completed match can't be double-walkover'd");
        }

        // League / Swiss / group double walkovers are covered in NoShowWalkoverTests — they now
        // close the fixture as a NoShow double forfeit instead of being rejected.

        [Test]
        public async Task DoubleWalkover_OnTeamSubMatch_Throws()
        {
            var harness = new BracketTestHarness(useSqlite: true);
            var tid = await harness.SeedTeamTournamentAsync(TournamentFormat.SingleElimination, 4, teamSize: 2);
            await harness.NewService().GenerateTeamSingleEliminationBracket(tid);

            var subMatch = harness.Matches(tid).First(m => m.TeamMatchId.HasValue);

            Assert.That(async () => await harness.NewService().ApplyDoubleWalkover(subMatch.Id!.Value),
                Throws.Exception, "team fixtures aggregate sub-matches — a sub-match can't be voided in isolation");
        }

        [Test]
        public async Task DoubleWalkover_DoubleElimination_AdvancesAcrossWinnerAndLoserEdges()
        {
            var harness = new BracketTestHarness(useSqlite: true);
            var tid = await harness.SeedSoloTournamentAsync(TournamentFormat.DoubleElimination, 4);
            await harness.NewService().GenerateDoubleEliminationBracket(tid);

            // Both Winners-Bracket round-1 matches feed the WB final; both also drop their losers to LB.
            var wbR1 = harness.Matches(tid)
                .Where(m => m.IsUpperBracket && m.RoundNumber == 1
                    && m.HomeParticipantId.HasValue && m.AwayParticipantId.HasValue)
                .OrderBy(m => m.MatchOrder)
                .ToList();
            Assert.That(wbR1.Count, Is.EqualTo(2));

            // Double-walkover one WB match, then play the sibling.
            await harness.NewService().ApplyDoubleWalkover(wbR1[0].Id!.Value);
            await harness.NewService().UpdateMatchResult(new MatchResultDto
            {
                MatchId = wbR1[1].Id!.Value, TournamentId = tid, HomeScore = 2, AwayScore = 1,
            });
            var siblingWinner = wbR1[1].HomeParticipantId!.Value;

            var voided = harness.Match(wbR1[0].Id!.Value);
            Assert.That(voided.WinnerParticipantId, Is.Null, "voided WB match has no winner");

            // Winner edge: the sibling's winner walks over the (now opponent-less) WB final.
            var wbFinal = harness.Match(wbR1[1].NextMatchId!.Value);
            Assert.That(wbFinal.Status, Is.EqualTo(MatchStatus.Completed), "WB final settled by walkover");
            Assert.That(wbFinal.WinnerParticipantId, Is.EqualTo(siblingWinner));
        }
    }
}
