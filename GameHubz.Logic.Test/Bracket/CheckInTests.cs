using System;
using System.Linq;
using System.Threading.Tasks;

using NUnit.Framework;

using GameHubz.Data.Context;
using GameHubz.DataModels.Consts;
using GameHubz.DataModels.Domain;
using GameHubz.DataModels.Enums;
using GameHubz.DataModels.Models;
using GameHubz.Logic.Exceptions;

namespace GameHubz.Logic.Test.Bracket
{
    // Ready check: both sides confirm they are at the keyboard for the time they agreed on, and the
    // side that turns up alone takes the match once the grace period runs out. These cover what the
    // feature can actually get wrong — the scoreline a forfeit is written as, that the bracket still
    // advances behind it, that nobody can be ruled on twice, and that a participant cannot report a
    // score while the check is still open. SQLite harness.
    [TestFixture]
    internal sealed class CheckInTests
    {
        private static void EnableCheckIn(BracketTestHarness harness, Guid tournamentId, int? graceMinutes = 10)
        {
            using var ctx = harness.ReadContext();
            var tournament = ctx.Set<TournamentEntity>().Single(t => t.Id == tournamentId);
            tournament.RequireMatchCheckIn = true;
            tournament.CheckInGraceMinutes = graceMinutes;
            ctx.SaveChanges();
        }

        /// <summary>Puts a match in the state the sweep finds: kick-off passed, one or both sides in.</summary>
        private static void MarkCheckedIn(
            BracketTestHarness harness, Guid matchId, DateTime kickOff, DateTime? home = null, DateTime? away = null)
        {
            using var ctx = harness.ReadContext();
            var match = ctx.Set<MatchEntity>().Single(m => m.Id == matchId);
            match.Status = MatchStatus.Scheduled;
            match.ScheduledStartTime = kickOff;
            match.HomeCheckedInOn = home;
            match.AwayCheckedInOn = away;
            ctx.SaveChanges();
        }

        // ── the scoreline ────────────────────────────────────────────────────────────────────

        [TestCase(1, 1, 0)]
        [TestCase(3, 2, 0)]
        [TestCase(5, 3, 0)]
        public async Task CheckInForfeit_ScoresTheWalkoverOverTheMatchesOwnBestOf(int bestOf, int winnerScore, int loserScore)
        {
            var harness = new BracketTestHarness(useSqlite: true);
            var tid = await harness.SeedSoloTournamentAsync(TournamentFormat.SingleElimination, 4, bestOf: bestOf);
            await harness.NewService().GenerateSingleEliminationBracket(tid);
            EnableCheckIn(harness, tid);

            var match = harness.Matches(tid).First(m => m.RoundNumber == 1);
            var kickOff = DateTime.UtcNow.AddMinutes(-30);
            MarkCheckedIn(harness, match.Id!.Value, kickOff, home: kickOff);

            var applied = await harness.NewService().ApplyCheckInForfeit(match.Id!.Value, homeWins: true);

            Assert.That(applied, Is.True);

            var settled = harness.Match(match.Id!.Value);
            Assert.That(settled.Status, Is.EqualTo(MatchStatus.Completed));
            Assert.That(settled.HomeUserScore, Is.EqualTo(winnerScore), "a forfeit is a clean walkover of the series");
            Assert.That(settled.AwayUserScore, Is.EqualTo(loserScore));
            Assert.That(settled.WinnerParticipantId, Is.EqualTo(match.HomeParticipantId));
            Assert.That(settled.CheckInResolvedOn, Is.Not.Null, "and the match is marked as ruled on");
        }

        [Test]
        public async Task CheckInForfeit_TotalScoreSeries_PlaysOutEveryGame()
        {
            // Aggregate-score tournaments only settle a series once every game of it exists, so the
            // forfeit has to write the full Best-of rather than just the games needed to clinch.
            var harness = new BracketTestHarness(useSqlite: true);
            var tid = await harness.SeedSoloTournamentAsync(
                TournamentFormat.SingleElimination, 4, bestOf: 3, seriesWinCondition: TeamWinCondition.AggregateScore);
            await harness.NewService().GenerateSingleEliminationBracket(tid);
            EnableCheckIn(harness, tid);

            var match = harness.Matches(tid).First(m => m.RoundNumber == 1);
            var kickOff = DateTime.UtcNow.AddMinutes(-30);
            MarkCheckedIn(harness, match.Id!.Value, kickOff, away: kickOff);

            await harness.NewService().ApplyCheckInForfeit(match.Id!.Value, homeWins: false);

            var settled = harness.Match(match.Id!.Value);
            Assert.That(settled.Status, Is.EqualTo(MatchStatus.Completed));
            Assert.That(settled.Games.Count, Is.EqualTo(3), "every game of the Bo3 is written");
            Assert.That(settled.AwayUserScore, Is.EqualTo(3), "3-0 on aggregate");
            Assert.That(settled.HomeUserScore, Is.EqualTo(0));
            Assert.That(settled.WinnerParticipantId, Is.EqualTo(match.AwayParticipantId));
        }

        // ── it is a real result, not a special case ──────────────────────────────────────────

        [Test]
        public async Task CheckInForfeit_AdvancesTheWinnerToTheNextRound()
        {
            var harness = new BracketTestHarness(useSqlite: true);
            var tid = await harness.SeedSoloTournamentAsync(TournamentFormat.SingleElimination, 4);
            await harness.NewService().GenerateSingleEliminationBracket(tid);
            EnableCheckIn(harness, tid);

            var semi = harness.Matches(tid).Where(m => m.RoundNumber == 1).OrderBy(m => m.MatchOrder).First();
            var kickOff = DateTime.UtcNow.AddMinutes(-30);
            MarkCheckedIn(harness, semi.Id!.Value, kickOff, home: kickOff);

            await harness.NewService().ApplyCheckInForfeit(semi.Id!.Value, homeWins: true);

            var final = harness.Match(semi.NextMatchId!.Value);
            Assert.That(
                new[] { final.HomeParticipantId, final.AwayParticipantId },
                Does.Contain(semi.HomeParticipantId),
                "the player who turned up carries on exactly as if he had won on the pitch");
        }

        [Test]
        public async Task CheckInForfeit_League_CountsAsAWinInTheStandings()
        {
            var harness = new BracketTestHarness(useSqlite: true);
            var tid = await harness.SeedSoloTournamentAsync(TournamentFormat.League, 4);
            await harness.NewService().GenerateLeagueTournament(tid);
            EnableCheckIn(harness, tid);

            var fixture = harness.Matches(tid).First(m => m.RoundNumber == 1);
            var kickOff = DateTime.UtcNow.AddMinutes(-20);
            MarkCheckedIn(harness, fixture.Id!.Value, kickOff, home: kickOff);

            await harness.NewService().ApplyCheckInForfeit(fixture.Id!.Value, homeWins: true);

            var participants = harness.Participants(tid);
            var winner = participants.Single(p => p.Id == fixture.HomeParticipantId);
            var loser = participants.Single(p => p.Id == fixture.AwayParticipantId);

            Assert.That(winner.Wins, Is.EqualTo(1), "the side that turned up is credited with the win");
            Assert.That(loser.Losses, Is.EqualTo(1));
            Assert.That(loser.Points, Is.EqualTo(0));
        }

        // ── nobody turned up ────────────────────────────────────────────────────────────────

        [Test]
        public async Task CheckInDoubleWalkover_League_ClosesTheFixtureWithNothingForEither()
        {
            var harness = new BracketTestHarness(useSqlite: true);
            var tid = await harness.SeedSoloTournamentAsync(TournamentFormat.League, 4);
            await harness.NewService().GenerateLeagueTournament(tid);
            EnableCheckIn(harness, tid);

            var fixture = harness.Matches(tid).First(m => m.RoundNumber == 1);
            MarkCheckedIn(harness, fixture.Id!.Value, DateTime.UtcNow.AddMinutes(-20));

            var applied = await harness.NewService().ApplyCheckInDoubleWalkover(fixture.Id!.Value);

            Assert.That(applied, Is.True);

            var closed = harness.Match(fixture.Id!.Value);
            Assert.That(closed.Status, Is.EqualTo(MatchStatus.NoShow), "a no-show, deliberately not a draw");
            Assert.That(closed.WinnerParticipantId, Is.Null);
            Assert.That(closed.CheckInResolvedOn, Is.Not.Null);

            var participants = harness.Participants(tid);
            foreach (var pid in new[] { fixture.HomeParticipantId!.Value, fixture.AwayParticipantId!.Value })
            {
                var p = participants.Single(x => x.Id == pid);
                Assert.That(p.Points, Is.EqualTo(0), "nobody played, so nobody scores");
                Assert.That(p.Draws, Is.EqualTo(0));
            }
        }

        [Test]
        public async Task CheckInDoubleWalkover_Elimination_LeavesNobodyStandingInThatSlot()
        {
            var harness = new BracketTestHarness(useSqlite: true);
            var tid = await harness.SeedSoloTournamentAsync(TournamentFormat.SingleElimination, 4);
            await harness.NewService().GenerateSingleEliminationBracket(tid);
            EnableCheckIn(harness, tid);

            var semi = harness.Matches(tid).Where(m => m.RoundNumber == 1).OrderBy(m => m.MatchOrder).First();
            MarkCheckedIn(harness, semi.Id!.Value, DateTime.UtcNow.AddMinutes(-20));

            await harness.NewService().ApplyCheckInDoubleWalkover(semi.Id!.Value);

            var voided = harness.Match(semi.Id!.Value);
            Assert.That(voided.Status, Is.EqualTo(MatchStatus.Completed));
            Assert.That(voided.WinnerParticipantId, Is.Null, "completed with no winner is the dead-feeder signal");
            Assert.That(voided.HomeUserScore, Is.Null);
        }

        [Test]
        public async Task CheckInDoubleWalkover_Final_StaysOpenAndUnclaimedForManualResolution()
        {
            var harness = new BracketTestHarness(useSqlite: true);
            var tid = await harness.SeedSoloTournamentAsync(TournamentFormat.SingleElimination, 4);
            await harness.NewService().GenerateSingleEliminationBracket(tid);
            EnableCheckIn(harness, tid);

            foreach (var semi in harness.Matches(tid).Where(m => m.RoundNumber == 1).ToList())
            {
                await harness.NewService().UpdateMatchResult(new MatchResultDto
                {
                    MatchId = semi.Id!.Value,
                    TournamentId = tid,
                    HomeScore = 2,
                    AwayScore = 0,
                });
            }

            var final = harness.Matches(tid).Single(m => m.RoundNumber == 2);
            MarkCheckedIn(harness, final.Id!.Value, DateTime.UtcNow.AddMinutes(-30));

            var applied = await harness.NewService().ApplyCheckInDoubleWalkover(final.Id!.Value);

            Assert.That(applied, Is.False, "a final cannot be voided automatically");
            var stillOpen = harness.Match(final.Id!.Value);
            Assert.That(stillOpen.Status, Is.EqualTo(MatchStatus.Scheduled));
            Assert.That(stillOpen.CheckInResolvedOn, Is.Null, "no verdict landed, so no claim may be stamped");
        }

        [Test]
        public async Task CheckInDoubleWalkover_PlayIn_StaysOpenAndUnclaimedForManualResolution()
        {
            var harness = new BracketTestHarness(useSqlite: true);
            var tid = await harness.SeedSoloTournamentAsync(
                TournamentFormat.Swiss, 6,
                swissRoundsCount: 1, swissKnockoutQualifiers: 4, swissDirectQualifiers: 2);
            await harness.NewService().GenerateSwissTournament(tid);
            EnableCheckIn(harness, tid);

            foreach (var swissMatch in harness.Matches(tid).Where(m => m.RoundNumber == 1).ToList())
            {
                await harness.NewService().UpdateMatchResult(new MatchResultDto
                {
                    MatchId = swissMatch.Id!.Value,
                    TournamentId = tid,
                    HomeScore = 2,
                    AwayScore = 0,
                });
            }

            var playInStage = harness.Stages(tid).Single(s => s.Type == StageType.PlayIn);
            var playIn = harness.Matches(tid).First(m => m.TournamentStageId == playInStage.Id);
            MarkCheckedIn(harness, playIn.Id!.Value, DateTime.UtcNow.AddMinutes(-30));

            var applied = await harness.NewService().ApplyCheckInDoubleWalkover(playIn.Id!.Value);

            Assert.That(applied, Is.False, "a play-in must produce a qualifier");
            var stillOpen = harness.Match(playIn.Id!.Value);
            Assert.That(stillOpen.Status, Is.EqualTo(MatchStatus.Scheduled));
            Assert.That(stillOpen.CheckInResolvedOn, Is.Null, "manual resolution must remain possible");
        }

        // ── ruled once, and only once ────────────────────────────────────────────────────────

        [Test]
        public async Task CheckInRuling_IsClaimedSoASecondSweepDoesNothing()
        {
            var harness = new BracketTestHarness(useSqlite: true);
            var tid = await harness.SeedSoloTournamentAsync(TournamentFormat.SingleElimination, 4);
            await harness.NewService().GenerateSingleEliminationBracket(tid);
            EnableCheckIn(harness, tid);

            var match = harness.Matches(tid).First(m => m.RoundNumber == 1);
            var kickOff = DateTime.UtcNow.AddMinutes(-30);
            MarkCheckedIn(harness, match.Id!.Value, kickOff, home: kickOff);

            Assert.That(await harness.NewService().ApplyCheckInForfeit(match.Id!.Value, homeWins: true), Is.True);
            Assert.That(
                await harness.NewService().ApplyCheckInForfeit(match.Id!.Value, homeWins: true), Is.False,
                "the match is no longer scheduled, and the stamp says it was already ruled on");
        }

        [Test]
        public async Task CheckInRuling_LeavesAMatchThatWasPlayedInTheMeantimeAlone()
        {
            var harness = new BracketTestHarness(useSqlite: true);
            var tid = await harness.SeedSoloTournamentAsync(TournamentFormat.League, 4);
            await harness.NewService().GenerateLeagueTournament(tid);
            EnableCheckIn(harness, tid);

            var fixture = harness.Matches(tid).First(m => m.RoundNumber == 1);
            var kickOff = DateTime.UtcNow.AddMinutes(-30);
            MarkCheckedIn(harness, fixture.Id!.Value, kickOff, home: kickOff);

            await harness.NewService().UpdateMatchResult(new MatchResultDto
            {
                MatchId = fixture.Id!.Value,
                TournamentId = tid,
                HomeScore = 1,
                AwayScore = 4,
            });

            Assert.That(
                await harness.NewService().ApplyCheckInForfeit(fixture.Id!.Value, homeWins: true), Is.False,
                "a real result beats the clock");
            Assert.That(harness.Match(fixture.Id!.Value).AwayUserScore, Is.EqualTo(4), "and stands untouched");
        }

        [Test]
        public async Task CheckInForfeit_RefusesWhenTheMissingSideTurnedUpInTheMeantime()
        {
            // The sweep lists a match, then rules on it a moment later. In between, the player it
            // was about to forfeit checked in — the ruling has to notice and stand down.
            var harness = new BracketTestHarness(useSqlite: true);
            var tid = await harness.SeedSoloTournamentAsync(TournamentFormat.League, 4);
            await harness.NewService().GenerateLeagueTournament(tid);
            EnableCheckIn(harness, tid);

            var fixture = harness.Matches(tid).First(m => m.RoundNumber == 1);
            var kickOff = DateTime.UtcNow.AddMinutes(-30);
            MarkCheckedIn(harness, fixture.Id!.Value, kickOff, home: kickOff, away: kickOff.AddMinutes(1));

            Assert.That(
                await harness.NewService().ApplyCheckInForfeit(fixture.Id!.Value, homeWins: true), Is.False,
                "both sides are in now - there is nothing to forfeit");
            Assert.That(harness.Match(fixture.Id!.Value).Status, Is.EqualTo(MatchStatus.Scheduled), "and the match is left to be played");
        }

        [Test]
        public async Task CheckInDoubleWalkover_RefusesWhenSomebodyTurnedUpInTheMeantime()
        {
            var harness = new BracketTestHarness(useSqlite: true);
            var tid = await harness.SeedSoloTournamentAsync(TournamentFormat.League, 4);
            await harness.NewService().GenerateLeagueTournament(tid);
            EnableCheckIn(harness, tid);

            var fixture = harness.Matches(tid).First(m => m.RoundNumber == 1);
            var kickOff = DateTime.UtcNow.AddMinutes(-30);
            MarkCheckedIn(harness, fixture.Id!.Value, kickOff, away: kickOff.AddMinutes(2));

            Assert.That(
                await harness.NewService().ApplyCheckInDoubleWalkover(fixture.Id!.Value), Is.False,
                "one player did turn up, so this is a forfeit and not a void");
            Assert.That(harness.Match(fixture.Id!.Value).CheckInResolvedOn, Is.Null, "and nothing was claimed");
        }

        // ── the report gate ─────────────────────────────────────────────────────────────────

        [Test]
        public async Task Report_IsRefusedWhileTheMatchIsStillWaitingOnACheckIn()
        {
            var harness = new BracketTestHarness(useSqlite: true);
            var tid = await harness.SeedSoloTournamentAsync(TournamentFormat.League, 4);
            await harness.NewService().GenerateLeagueTournament(tid);
            EnableCheckIn(harness, tid);

            var fixture = harness.Matches(tid).First(m => m.RoundNumber == 1);
            var kickOff = DateTime.UtcNow.AddMinutes(-5);
            MarkCheckedIn(harness, fixture.Id!.Value, kickOff, home: kickOff);

            var playerId = harness.ParticipantUserId(fixture.HomeParticipantId!.Value);
            await harness.DenyManageFor(playerId, tid);

            Assert.ThrowsAsync<BusinessRuleException>(async () =>
                await harness.NewServiceAsUser(playerId).UpdateMatchResult(new MatchResultDto
                {
                    MatchId = fixture.Id!.Value,
                    TournamentId = tid,
                    HomeScore = 3,
                    AwayScore = 0,
                }),
                "one player's word is not a result - that is the whole point of the check");
        }

        [Test]
        public async Task Report_IsAllowedBeforeTheCheckInWindowOpens()
        {
            var harness = new BracketTestHarness(useSqlite: true);
            var tid = await harness.SeedSoloTournamentAsync(TournamentFormat.League, 4);
            await harness.NewService().GenerateLeagueTournament(tid);
            EnableCheckIn(harness, tid);

            var fixture = harness.Matches(tid).First(m => m.RoundNumber == 1);
            MarkCheckedIn(harness, fixture.Id!.Value, DateTime.UtcNow.AddHours(2));

            var playerId = harness.ParticipantUserId(fixture.HomeParticipantId!.Value);
            await harness.DenyManageFor(playerId, tid);

            await harness.NewServiceAsUser(playerId).UpdateMatchResult(new MatchResultDto
            {
                MatchId = fixture.Id!.Value,
                TournamentId = tid,
                HomeScore = 3,
                AwayScore = 0,
            });

            Assert.That(harness.Match(fixture.Id!.Value).Status, Is.EqualTo(MatchStatus.Completed),
                "a match played early must be reportable before either side can check in");
        }

        [Test]
        public async Task Proposal_IsRefusedWhenTheProposerNeverCheckedIn()
        {
            // The hole this closes: the opponent is in, the proposer is about to lose by forfeit, and a
            // made-up score would have closed the ready check and made that forfeit never come.
            var harness = new BracketTestHarness(useSqlite: true);
            var tid = await harness.SeedSoloTournamentAsync(
                TournamentFormat.League, 4, requireResultApproval: true);
            await harness.NewService().GenerateLeagueTournament(tid);
            EnableCheckIn(harness, tid);

            var fixture = harness.Matches(tid).First(m => m.RoundNumber == 1);
            var kickOff = DateTime.UtcNow.AddMinutes(-5);
            MarkCheckedIn(harness, fixture.Id!.Value, kickOff, away: kickOff);

            var proposerId = harness.ParticipantUserId(fixture.HomeParticipantId!.Value);
            await harness.DenyManageFor(proposerId, tid);

            Assert.ThrowsAsync<BusinessRuleException>(async () =>
                await harness.NewServiceAsUser(proposerId).UpdateMatchResult(new MatchResultDto
                {
                    MatchId = fixture.Id!.Value,
                    TournamentId = tid,
                    HomeScore = 3,
                    AwayScore = 0,
                }),
                "a player who never checked in must not be able to propose a score");

            var after = harness.Match(fixture.Id!.Value);
            Assert.That(after.ProposedByUserId, Is.Null);
            Assert.That(after.CheckInResolvedOn, Is.Null, "the ready check stays open, so the forfeit still comes");
        }

        [Test]
        public async Task Proposal_IsAllowedWhenTheProposerCheckedIn_EvenIfTheOpponentDidNot()
        {
            var harness = new BracketTestHarness(useSqlite: true);
            var tid = await harness.SeedSoloTournamentAsync(
                TournamentFormat.League, 4, requireResultApproval: true);
            await harness.NewService().GenerateLeagueTournament(tid);
            EnableCheckIn(harness, tid);

            var fixture = harness.Matches(tid).First(m => m.RoundNumber == 1);
            var kickOff = DateTime.UtcNow.AddMinutes(-30);
            MarkCheckedIn(harness, fixture.Id!.Value, kickOff, home: kickOff);

            var proposerId = harness.ParticipantUserId(fixture.HomeParticipantId!.Value);
            var opponentId = harness.ParticipantUserId(fixture.AwayParticipantId!.Value);
            await harness.DenyManageFor(proposerId, tid);
            await harness.DenyManageFor(opponentId, tid);

            await harness.NewServiceAsUser(proposerId).UpdateMatchResult(new MatchResultDto
            {
                MatchId = fixture.Id!.Value,
                TournamentId = tid,
                HomeScore = 3,
                AwayScore = 0,
            });

            Assert.That(harness.Match(fixture.Id!.Value).ProposedByUserId, Is.EqualTo(proposerId),
                "the first report remains only a proposal");

            await harness.NewServiceAsUser(opponentId).ApproveProposedResult(fixture.Id!.Value);

            Assert.That(harness.Match(fixture.Id!.Value).Status, Is.EqualTo(MatchStatus.Completed),
                "opponent confirmation is sufficient even when the sweep missed the ready check");
        }

        [Test]
        public async Task Report_IsAllowedOnceBothSidesAreIn()
        {
            var harness = new BracketTestHarness(useSqlite: true);
            var tid = await harness.SeedSoloTournamentAsync(TournamentFormat.League, 4);
            await harness.NewService().GenerateLeagueTournament(tid);
            EnableCheckIn(harness, tid);

            var fixture = harness.Matches(tid).First(m => m.RoundNumber == 1);
            var kickOff = DateTime.UtcNow.AddMinutes(-5);
            MarkCheckedIn(harness, fixture.Id!.Value, kickOff, home: kickOff, away: kickOff.AddMinutes(1));

            var playerId = harness.ParticipantUserId(fixture.HomeParticipantId!.Value);
            await harness.DenyManageFor(playerId, tid);

            await harness.NewServiceAsUser(playerId).UpdateMatchResult(new MatchResultDto
            {
                MatchId = fixture.Id!.Value,
                TournamentId = tid,
                HomeScore = 3,
                AwayScore = 0,
            });

            Assert.That(harness.Match(fixture.Id!.Value).Status, Is.EqualTo(MatchStatus.Completed));
        }

        [Test]
        public async Task Report_IsAlwaysOpenToAnOrganizer()
        {
            // The escape hatch: the pair played, nobody pressed anything, and the organizer has to
            // be able to enter what happened.
            var harness = new BracketTestHarness(useSqlite: true);
            var tid = await harness.SeedSoloTournamentAsync(TournamentFormat.League, 4);
            await harness.NewService().GenerateLeagueTournament(tid);
            EnableCheckIn(harness, tid);

            var fixture = harness.Matches(tid).First(m => m.RoundNumber == 1);
            MarkCheckedIn(harness, fixture.Id!.Value, DateTime.UtcNow.AddMinutes(-5));

            await harness.NewService().UpdateMatchResult(new MatchResultDto
            {
                MatchId = fixture.Id!.Value,
                TournamentId = tid,
                HomeScore = 2,
                AwayScore = 2,
            });

            Assert.That(harness.Match(fixture.Id!.Value).Status, Is.EqualTo(MatchStatus.Completed));
        }

        [Test]
        public async Task Report_IsUntouchedWhenTheMatchHasNoAgreedTime()
        {
            // A pair who arranged the match in chat never had a check-in to run, so the gate must
            // not lock them out of their own result.
            var harness = new BracketTestHarness(useSqlite: true);
            var tid = await harness.SeedSoloTournamentAsync(TournamentFormat.League, 4);
            await harness.NewService().GenerateLeagueTournament(tid);
            EnableCheckIn(harness, tid);

            var fixture = harness.Matches(tid).First(m => m.RoundNumber == 1);
            var playerId = harness.ParticipantUserId(fixture.HomeParticipantId!.Value);
            await harness.DenyManageFor(playerId, tid);

            await harness.NewServiceAsUser(playerId).UpdateMatchResult(new MatchResultDto
            {
                MatchId = fixture.Id!.Value,
                TournamentId = tid,
                HomeScore = 5,
                AwayScore = 2,
            });

            Assert.That(harness.Match(fixture.Id!.Value).Status, Is.EqualTo(MatchStatus.Completed));
        }

        // ── checking in ─────────────────────────────────────────────────────────────────────

        [Test]
        public async Task CheckIn_StampsTheCallersSide_AndIsIdempotent()
        {
            var harness = new BracketTestHarness(useSqlite: true);
            var tid = await harness.SeedSoloTournamentAsync(TournamentFormat.League, 4);
            await harness.NewService().GenerateLeagueTournament(tid);
            EnableCheckIn(harness, tid);

            var fixture = harness.Matches(tid).First(m => m.RoundNumber == 1);
            harness.MarkScheduled(fixture.Id!.Value, DateTime.UtcNow.AddMinutes(2));

            var playerId = harness.ParticipantUserId(fixture.HomeParticipantId!.Value);

            var first = await harness.NewMatchServiceAsUser(playerId).CheckIn(fixture.Id!.Value);
            Assert.That(first.IsHome, Is.True);
            Assert.That(first.HomeCheckedInOn, Is.Not.Null);
            Assert.That(first.AwayCheckedInOn, Is.Null);

            var second = await harness.NewMatchServiceAsUser(playerId).CheckIn(fixture.Id!.Value);
            Assert.That(
                second.HomeCheckedInOn, Is.EqualTo(first.HomeCheckedInOn),
                "the stamp is evidence of when he arrived, not a toggle");
        }

        [Test]
        public async Task CheckIn_IsRefusedBeforeTheWindowOpens()
        {
            var harness = new BracketTestHarness(useSqlite: true);
            var tid = await harness.SeedSoloTournamentAsync(TournamentFormat.League, 4);
            await harness.NewService().GenerateLeagueTournament(tid);
            EnableCheckIn(harness, tid);

            var fixture = harness.Matches(tid).First(m => m.RoundNumber == 1);
            harness.MarkScheduled(fixture.Id!.Value, DateTime.UtcNow.AddHours(3));

            var playerId = harness.ParticipantUserId(fixture.HomeParticipantId!.Value);

            Assert.ThrowsAsync<BusinessRuleException>(
                async () => await harness.NewMatchServiceAsUser(playerId).CheckIn(fixture.Id!.Value),
                "checking in a day early would defeat the point of confirming you are there");
        }

        [Test]
        public async Task CheckIn_IsRefusedOnceTheDeadlineHasPassed()
        {
            var harness = new BracketTestHarness(useSqlite: true);
            var tid = await harness.SeedSoloTournamentAsync(TournamentFormat.League, 4);
            await harness.NewService().GenerateLeagueTournament(tid);
            EnableCheckIn(harness, tid, graceMinutes: 5);

            var fixture = harness.Matches(tid).First(m => m.RoundNumber == 1);
            harness.MarkScheduled(fixture.Id!.Value, DateTime.UtcNow.AddMinutes(-30));

            var playerId = harness.ParticipantUserId(fixture.HomeParticipantId!.Value);

            Assert.ThrowsAsync<BusinessRuleException>(
                async () => await harness.NewMatchServiceAsUser(playerId).CheckIn(fixture.Id!.Value),
                "the sweep owns the match by then");
        }

        [Test]
        public async Task CheckIn_IsRefusedForSomebodyElsesMatch()
        {
            var harness = new BracketTestHarness(useSqlite: true);
            var tid = await harness.SeedSoloTournamentAsync(TournamentFormat.League, 4);
            await harness.NewService().GenerateLeagueTournament(tid);
            EnableCheckIn(harness, tid);

            var round1 = harness.Matches(tid).Where(m => m.RoundNumber == 1).OrderBy(m => m.MatchOrder).ToList();
            harness.MarkScheduled(round1[0].Id!.Value, DateTime.UtcNow.AddMinutes(2));

            var strangerId = harness.ParticipantUserId(round1[1].HomeParticipantId!.Value);

            Assert.ThrowsAsync<BusinessRuleException>(
                async () => await harness.NewMatchServiceAsUser(strangerId).CheckIn(round1[0].Id!.Value));
        }

        // ── team ties: the check belongs to the player, not the team ────────────────────────

        [Test]
        public async Task CheckIn_TeamGame_OnlyTheNominatedPlayerMayConfirm()
        {
            // A tie is checked in game by game, by the two players actually sitting down to play
            // it. Not the captain, not a team-mate: a captain confirming for someone who is not
            // there would win his team a game it never turned up for.
            var harness = new BracketTestHarness(useSqlite: true);
            var tid = await harness.SeedTeamTournamentAsync(TournamentFormat.League, teamCount: 2, teamSize: 2);
            await harness.NewService().GenerateTeamLeagueTournament(tid);
            EnableCheckIn(harness, tid);

            var subMatch = harness.Matches(tid).First(m => m.TeamMatchId.HasValue);

            var nominatedPlayer = Guid.NewGuid();
            var captain = Guid.NewGuid();
            var teamId = Guid.NewGuid();
            await harness.SeedTeamRosterAsync(teamId, tid, captainUserId: captain, otherMemberUserIds: nominatedPlayer);

            // Wire the roster to the fixture: the home side is that team, and the player fielded
            // for THIS game is the nominated one.
            using (var ctx = harness.ReadContext())
            {
                var match = ctx.Set<MatchEntity>().Single(m => m.Id == subMatch.Id);
                match.HomeUserId = nominatedPlayer;
                match.AwayUserId = Guid.NewGuid();
                match.Status = MatchStatus.Scheduled;
                match.ScheduledStartTime = DateTime.UtcNow.AddMinutes(2);

                var participant = ctx.Set<TournamentParticipantEntity>().Single(p => p.Id == match.HomeParticipantId);
                participant.TeamId = teamId;

                ctx.SaveChanges();
            }

            Assert.ThrowsAsync<BusinessRuleException>(
                async () => await harness.NewMatchServiceAsUser(captain).CheckIn(subMatch.Id!.Value),
                "the captain is on the roster, but he is not the one playing this game");

            var result = await harness.NewMatchServiceAsUser(nominatedPlayer).CheckIn(subMatch.Id!.Value);
            Assert.That(result.IsHome, Is.True);
            Assert.That(result.HomeCheckedInOn, Is.Not.Null, "the player fielded for the game answers for it");
        }

        // ── the arithmetic everything else leans on ─────────────────────────────────────────

        [Test]
        public void Deadline_RunsFromKickOff_WhenTheFirstSideIsPunctual()
        {
            var kickOff = new DateTime(2026, 9, 7, 17, 0, 0, DateTimeKind.Utc);

            // Confirmed a quarter of an hour early: the clock still starts at kick-off, so an eager
            // player cannot burn the window his opponent was promised.
            var deadline = MatchCheckInRules.Deadline(kickOff, kickOff.AddMinutes(-15), null, 10);

            Assert.That(deadline, Is.EqualTo(kickOff.AddMinutes(10)));
        }

        [Test]
        public void Deadline_RunsFromTheFirstCheckIn_WhenThatSideWasLateItself()
        {
            var kickOff = new DateTime(2026, 9, 7, 17, 0, 0, DateTimeKind.Utc);
            var lateArrival = kickOff.AddMinutes(6);

            var deadline = MatchCheckInRules.Deadline(kickOff, null, lateArrival, 10);

            Assert.That(
                deadline, Is.EqualTo(lateArrival.AddMinutes(10)),
                "a player who is late himself does not get to shorten the other's grace");
        }

        [Test]
        public void Deadline_WithNobodyIn_IsKickOffPlusGrace()
        {
            var kickOff = new DateTime(2026, 9, 7, 17, 0, 0, DateTimeKind.Utc);

            Assert.That(MatchCheckInRules.Deadline(kickOff, null, null, 10), Is.EqualTo(kickOff.AddMinutes(10)));
        }

        [Test]
        public void Grace_FallsBackToTheDefault_AndStaysInsideItsBounds()
        {
            Assert.That(MatchCheckInRules.ResolveGraceMinutes(null), Is.EqualTo(MatchCheckInRules.DefaultGraceMinutes));
            Assert.That(MatchCheckInRules.ResolveGraceMinutes(0), Is.EqualTo(MatchCheckInRules.MinGraceMinutes));
            Assert.That(MatchCheckInRules.ResolveGraceMinutes(9999), Is.EqualTo(MatchCheckInRules.MaxGraceMinutes));
        }

        // ── "we agreed outside the app" ──────────────────────────────────────────────────────

        // SetScheduled stamps the kick-off as *now* and tells the opponent nothing. Run as an
        // ordinary fixture that has already started, the ready check would give that opponent one
        // grace period to answer a message he never got, then take the match off him.
        [Test]
        public async Task AgreedOutsideTheApp_ClosesTheReadyCheckInsteadOfStartingOne()
        {
            var harness = new BracketTestHarness(useSqlite: true);
            var tid = await harness.SeedSoloTournamentAsync(TournamentFormat.League, 4);
            await harness.NewService().GenerateLeagueTournament(tid);
            EnableCheckIn(harness, tid);

            var match = harness.Matches(tid).First(m => m.RoundNumber == 1);
            var playerId = harness.ParticipantUserId(match.HomeParticipantId!.Value);

            await harness.NewMatchServiceAsUser(playerId).SetScheduled(match.Id!.Value);

            var scheduled = harness.Match(match.Id!.Value);
            Assert.That(scheduled.Status, Is.EqualTo(MatchStatus.Scheduled));
            Assert.That(
                scheduled.CheckInResolvedOn, Is.Not.Null,
                "the pair sorted this out between themselves — there is no check to run");

            // The sweep's claim is what it would need to rule, and the match is already spoken for.
            Assert.That(
                await harness.NewService().ApplyCheckInDoubleWalkover(match.Id!.Value), Is.False,
                "and no sweep may void a fixture the players arranged themselves");

            Assert.That(harness.Match(match.Id!.Value).Status, Is.EqualTo(MatchStatus.Scheduled));
        }

        // The marker says "this kick-off has been ruled on". Cancelling the kick-off has to take it
        // with it, or the fixture is quietly exempt from the check for the rest of the tournament.
        [Test]
        public async Task ClearSchedule_TakesTheVerdictMarkerWithTheKickOff()
        {
            var harness = new BracketTestHarness(useSqlite: true);
            var tid = await harness.SeedSoloTournamentAsync(TournamentFormat.League, 4);
            await harness.NewService().GenerateLeagueTournament(tid);
            EnableCheckIn(harness, tid);

            var match = harness.Matches(tid).First(m => m.RoundNumber == 1);
            var playerId = harness.ParticipantUserId(match.HomeParticipantId!.Value);

            await harness.NewMatchServiceAsUser(playerId).SetScheduled(match.Id!.Value);
            Assert.That(harness.Match(match.Id!.Value).CheckInResolvedOn, Is.Not.Null, "precondition");

            await harness.NewMatchServiceAsUser(BracketTestHarness.OwnerUserId, "Admin")
                .ClearSchedule(match.Id!.Value);

            var cleared = harness.Match(match.Id!.Value);
            Assert.That(cleared.Status, Is.EqualTo(MatchStatus.Pending));
            Assert.That(cleared.CheckInResolvedOn, Is.Null, "the next kick-off gets its own ready check");
            Assert.That(cleared.HomeCheckedInOn, Is.Null);
            Assert.That(cleared.AwayCheckedInOn, Is.Null);
        }
    }
}
