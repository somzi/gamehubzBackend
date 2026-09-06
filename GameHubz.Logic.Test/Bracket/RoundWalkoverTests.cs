using System;
using System.Linq;
using System.Threading.Tasks;

using NUnit.Framework;

using GameHubz.DataModels.Enums;
using GameHubz.DataModels.Models;
using GameHubz.Logic.Exceptions;

namespace GameHubz.Logic.Test.Bracket
{
    // Round-wide double walkover: the organizer closes every SOLO fixture of ONE round that was
    // never played, instead of opening each one by hand after a deadline passes. Same outcome per
    // fixture as ApplyDoubleWalkover (see DoubleWalkoverTests / NoShowWalkoverTests) — these tests
    // are about what only the bulk path can get wrong: which fixtures it picks up, which it must
    // leave alone, and that the advancement hooks still run once for the round. SQLite harness.
    [TestFixture]
    internal sealed class RoundWalkoverTests
    {
        [Test]
        public async Task RoundWalkover_League_ClosesOnlyTheUnplayedFixtures_AndUnlocksTheNextRound()
        {
            var harness = new BracketTestHarness(useSqlite: true);
            var tid = await harness.SeedSoloTournamentAsync(TournamentFormat.League, 4);
            await harness.NewService().GenerateLeagueTournament(tid);

            var round1 = harness.Matches(tid).Where(m => m.RoundNumber == 1).OrderBy(m => m.MatchOrder).ToList();
            Assert.That(round1.Count, Is.EqualTo(2));
            var stageId = round1[0].TournamentStageId!.Value;

            // One fixture is played for real; the other is the one nobody turned up for.
            await harness.NewService().UpdateMatchResult(new MatchResultDto
            {
                MatchId = round1[0].Id!.Value, TournamentId = tid, HomeScore = 3, AwayScore = 1,
            });

            var result = await harness.NewService().ApplyRoundWalkover(stageId, 1);

            Assert.That(result.Closed, Is.EqualTo(1), "only the unplayed fixture is closed");
            Assert.That(result.Skipped, Is.EqualTo(0));

            var played = harness.Match(round1[0].Id!.Value);
            Assert.That(played.Status, Is.EqualTo(MatchStatus.Completed), "the played fixture is untouched");
            Assert.That(played.HomeUserScore, Is.EqualTo(3), "and keeps its score");

            var closed = harness.Match(round1[1].Id!.Value);
            Assert.That(closed.Status, Is.EqualTo(MatchStatus.NoShow), "closed as a no-show, not completed");
            Assert.That(closed.WinnerParticipantId, Is.Null);

            // The whole point of NoShow over Completed-with-no-winner: no draw points.
            var participants = harness.Participants(tid);
            foreach (var pid in new[] { round1[1].HomeParticipantId!.Value, round1[1].AwayParticipantId!.Value })
            {
                var p = participants.Single(x => x.Id == pid);
                Assert.That(p.Points, Is.EqualTo(0), "a double forfeit awards nothing");
                Assert.That(p.Draws, Is.EqualTo(0), "and is not a draw");
            }

            // The round is complete now, so the advancement hook must have opened round 2 — the
            // reason the bulk path runs the hooks at all rather than just rewriting rows.
            var round2 = harness.Matches(tid).Where(m => m.RoundNumber == 2).ToList();
            Assert.That(round2, Is.Not.Empty);
            Assert.That(round2.All(m => m.RoundOpenAt <= DateTime.UtcNow), Is.True, "round 2 unlocked");
        }

        [Test]
        public async Task RoundWalkover_LeavesAPendingProposalAlone()
        {
            var harness = new BracketTestHarness(useSqlite: true);
            var tid = await harness.SeedSoloTournamentAsync(TournamentFormat.League, 4, requireResultApproval: true);
            await harness.NewService().GenerateLeagueTournament(tid);

            var round1 = harness.Matches(tid).Where(m => m.RoundNumber == 1).OrderBy(m => m.MatchOrder).ToList();
            var stageId = round1[0].TournamentStageId!.Value;

            // A player reported a result on the first fixture; it is waiting for approval.
            var proposerUserId = harness.ParticipantUserId(round1[0].HomeParticipantId!.Value);
            await harness.DenyManageFor(proposerUserId, tid);
            await harness.NewServiceAsUser(proposerUserId).UpdateMatchResult(new MatchResultDto
            {
                MatchId = round1[0].Id!.Value, TournamentId = tid, HomeScore = 2, AwayScore = 0,
            });

            var result = await harness.NewService().ApplyRoundWalkover(stageId, 1);

            Assert.That(result.Closed, Is.EqualTo(1), "only the fixture nobody reported on");
            Assert.That(result.Skipped, Is.EqualTo(1), "the proposal is reported back, not swallowed");

            var proposed = harness.Match(round1[0].Id!.Value);
            Assert.That(proposed.Status, Is.Not.EqualTo(MatchStatus.NoShow), "a reported result is never bulk-voided");
            Assert.That(proposed.ProposedByUserId, Is.EqualTo(proposerUserId), "the proposal survives untouched");

            Assert.That(harness.Match(round1[1].Id!.Value).Status, Is.EqualTo(MatchStatus.NoShow));

            // Round 1 still owes the proposal, so nothing may unlock behind it.
            var round2 = harness.Matches(tid).Where(m => m.RoundNumber == 2).ToList();
            Assert.That(round2.All(m => m.RoundOpenAt > DateTime.UtcNow), Is.True, "round 2 stays locked");
        }

        [Test]
        public async Task RoundWalkover_Elimination_VoidsTheUnplayedHalf_AndCascades()
        {
            var harness = new BracketTestHarness(useSqlite: true);
            var tid = await harness.SeedSoloTournamentAsync(TournamentFormat.SingleElimination, 8);
            await harness.NewService().GenerateSingleEliminationBracket(tid);

            var quarters = harness.Matches(tid).Where(m => m.RoundNumber == 1).OrderBy(m => m.MatchOrder).ToList();
            Assert.That(quarters.Count, Is.EqualTo(4));
            var stageId = quarters[0].TournamentStageId!.Value;

            // The top half played; the bottom half never showed up.
            foreach (var quarter in quarters.Take(2))
            {
                await harness.NewService().UpdateMatchResult(new MatchResultDto
                {
                    MatchId = quarter.Id!.Value, TournamentId = tid, HomeScore = 2, AwayScore = 1,
                });
            }

            var result = await harness.NewService().ApplyRoundWalkover(stageId, 1);
            Assert.That(result.Closed, Is.EqualTo(2), "the two unplayed quarter-finals");

            foreach (var quarter in quarters.Skip(2))
            {
                var voided = harness.Match(quarter.Id!.Value);
                Assert.That(voided.Status, Is.EqualTo(MatchStatus.Completed), "elimination voids close completed");
                Assert.That(voided.WinnerParticipantId, Is.Null, "with no winner — the dead-feeder signal");
            }

            // Both feeders of the bottom semi-final are dead, so the settle pass voids it too.
            var bottomSemi = harness.Match(quarters[2].NextMatchId!.Value);
            Assert.That(bottomSemi.Status, Is.EqualTo(MatchStatus.Completed));
            Assert.That(bottomSemi.WinnerParticipantId, Is.Null, "nobody survives the voided half");

            // The live half still has a semi-final to play, so no champion yet.
            Assert.That(harness.Tournament(tid).Status, Is.Not.EqualTo(TournamentStatus.Completed));

            // Playing it hands its winner the final by walkover — the cascade the void set up.
            var topSemi = harness.Match(quarters[0].NextMatchId!.Value);
            await harness.NewService().UpdateMatchResult(new MatchResultDto
            {
                MatchId = topSemi.Id!.Value, TournamentId = tid, HomeScore = 2, AwayScore = 0,
            });

            var final = harness.Match(topSemi.NextMatchId!.Value);
            Assert.That(final.Status, Is.EqualTo(MatchStatus.Completed));
            Assert.That(final.WinnerParticipantId, Is.EqualTo(topSemi.HomeParticipantId), "walkover into the final");
            Assert.That(harness.Tournament(tid).Status, Is.EqualTo(TournamentStatus.Completed));
        }

        [Test]
        public async Task RoundWalkover_SkipsTheFinal_WhichHasNowhereToAdvance()
        {
            var harness = new BracketTestHarness(useSqlite: true);
            var tid = await harness.SeedSoloTournamentAsync(TournamentFormat.SingleElimination, 4);
            await harness.NewService().GenerateSingleEliminationBracket(tid);

            var semis = harness.Matches(tid).Where(m => m.RoundNumber == 1).OrderBy(m => m.MatchOrder).ToList();
            foreach (var semi in semis)
            {
                await harness.NewService().UpdateMatchResult(new MatchResultDto
                {
                    MatchId = semi.Id!.Value, TournamentId = tid, HomeScore = 2, AwayScore = 1,
                });
            }

            var final = harness.Matches(tid).Single(m => m.RoundNumber == 2);
            var result = await harness.NewService().ApplyRoundWalkover(final.TournamentStageId!.Value, 2);

            Assert.That(result.Closed, Is.EqualTo(0), "a walkover in the final would advance nobody");
            Assert.That(result.Skipped, Is.EqualTo(1), "and the organizer is told it was left open");
            Assert.That(harness.Match(final.Id!.Value).Status, Is.Not.EqualTo(MatchStatus.Completed));
        }

        [Test]
        public async Task RoundWalkover_LeavesTeamTiesAlone()
        {
            var harness = new BracketTestHarness(useSqlite: true);
            var tid = await harness.SeedTeamTournamentAsync(TournamentFormat.League, 2, teamSize: 2);
            await harness.NewService().GenerateTeamLeagueTournament(tid);

            var fixture = harness.TeamMatches(tid).Single();
            var subs = harness.Matches(tid).Where(m => m.TeamMatchId == fixture.Id).ToList();
            Assert.That(subs.Count, Is.GreaterThan(1), "a tie holds several games");

            var result = await harness.NewService().ApplyRoundWalkover(subs[0].TournamentStageId!.Value, 1);

            // Voiding a whole tie is a per-game decision the organizer makes in the match modal, so
            // the round action closes nothing here and says so.
            Assert.That(result.Closed, Is.EqualTo(0), "team games are out of scope");
            Assert.That(result.Skipped, Is.EqualTo(1), "counted as the ONE fixture the organizer sees");

            Assert.That(harness.Matches(tid).Where(m => m.TeamMatchId == fixture.Id)
                .All(m => m.Status != MatchStatus.NoShow), Is.True, "no game was closed");
            Assert.That(harness.TeamMatches(tid).Single().Status, Is.EqualTo(TeamMatchStatus.Pending),
                "the tie is untouched");
        }

        [Test]
        public async Task RoundWalkover_ByANonManager_Throws()
        {
            var harness = new BracketTestHarness(useSqlite: true);
            var tid = await harness.SeedSoloTournamentAsync(TournamentFormat.League, 4);
            await harness.NewService().GenerateLeagueTournament(tid);

            var round1 = harness.Matches(tid).Where(m => m.RoundNumber == 1).ToList();
            var stageId = round1[0].TournamentStageId!.Value;

            var playerUserId = harness.ParticipantUserId(round1[0].HomeParticipantId!.Value);
            await harness.DenyManageFor(playerUserId, tid);

            Assert.That(async () => await harness.NewServiceAsUser(playerUserId).ApplyRoundWalkover(stageId, 1),
                Throws.TypeOf<BusinessRuleException>());

            Assert.That(harness.Matches(tid).Where(m => m.RoundNumber == 1)
                .All(m => m.Status != MatchStatus.NoShow), Is.True, "nothing was closed");
        }
    }
}
