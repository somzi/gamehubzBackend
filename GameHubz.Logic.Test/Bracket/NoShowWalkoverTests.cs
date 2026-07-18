using System;
using System.Linq;
using System.Threading.Tasks;

using NUnit.Framework;

using GameHubz.DataModels.Enums;
using GameHubz.DataModels.Models;
using GameHubz.Logic.Exceptions;

namespace GameHubz.Logic.Test.Bracket
{
    // Double walkover on group-machinery formats (Swiss / League / GroupStage): the fixture closes
    // as NoShow — a double forfeit awarding NOTHING to either player (deliberately not a draw) —
    // while the round still pairs / unlocks / completes as if it had been played. Also covers the
    // undo (delete-result reopens), the late real result (overwrites the NoShow), and the play-in
    // rejection. SQLite harness.
    [TestFixture]
    internal sealed class NoShowWalkoverTests
    {
        [Test]
        public async Task SwissNoShow_AwardsNothing_AndTheRoundStillPairs()
        {
            var harness = new BracketTestHarness(useSqlite: true);
            var tournamentId = await harness.SeedSoloTournamentAsync(TournamentFormat.Swiss, 4);
            await harness.NewService().GenerateSwissTournament(tournamentId);

            var round1 = harness.Matches(tournamentId).Where(m => m.RoundNumber == 1).ToList();
            var noShowMatch = round1[0];
            var playedMatch = round1[1];

            await harness.NewService().UpdateMatchResult(new MatchResultDto
            {
                MatchId = playedMatch.Id!.Value, TournamentId = tournamentId, HomeScore = 2, AwayScore = 0,
            });

            // The walkover on the last open fixture must close the round and pair round 2.
            await harness.NewService().ApplyDoubleWalkover(noShowMatch.Id!.Value);

            var closed = harness.Match(noShowMatch.Id!.Value);
            Assert.That(closed.Status, Is.EqualTo(MatchStatus.NoShow), "closed as a no-show, not completed");
            Assert.That(closed.WinnerParticipantId, Is.Null);

            // The whole point: NO draw points. Both no-show players stay at zero.
            var participants = harness.Participants(tournamentId);
            foreach (var pid in new[] { noShowMatch.HomeParticipantId!.Value, noShowMatch.AwayParticipantId!.Value })
            {
                var p = participants.Single(x => x.Id == pid);
                Assert.That(p.Points, Is.EqualTo(0), "a double forfeit awards no points");
                Assert.That(p.Draws, Is.EqualTo(0), "and is not a draw");
            }

            var round2 = harness.Matches(tournamentId).Where(m => m.RoundNumber == 2).ToList();
            Assert.That(round2.Count, Is.EqualTo(2), "round 2 paired despite the no-show fixture");

            // The no-show pair still counts as "already paired" — Swiss must not rematch them.
            bool rematched = round2.Any(m =>
                (m.HomeParticipantId == noShowMatch.HomeParticipantId && m.AwayParticipantId == noShowMatch.AwayParticipantId)
                || (m.HomeParticipantId == noShowMatch.AwayParticipantId && m.AwayParticipantId == noShowMatch.HomeParticipantId));
            Assert.That(rematched, Is.False, "the no-show pair is not paired again");
        }

        [Test]
        public async Task SwissNoShow_OnFinalRound_CompletesTheTournament()
        {
            var harness = new BracketTestHarness(useSqlite: true);
            var tournamentId = await harness.SeedSoloTournamentAsync(TournamentFormat.Swiss, 4);
            await harness.NewService().GenerateSwissTournament(tournamentId);

            // Round 1 fully played, round 2: one played, one no-show.
            foreach (var m in harness.Matches(tournamentId).Where(m => m.RoundNumber == 1))
            {
                await harness.NewService().UpdateMatchResult(new MatchResultDto
                {
                    MatchId = m.Id!.Value, TournamentId = tournamentId, HomeScore = 2, AwayScore = 0,
                });
            }

            // Round 2 pairs the two R1 winners together and the two losers together, but
            // Matches() returns them in no guaranteed order. Deterministically play the
            // WINNERS' match and void the losers' — the played winner then tops the table
            // alone on 6 points. (Playing the losers' match instead leaves a three-way tie
            // on 3 points, where the backend's Buchholz/H2H tie-break — not a plain points
            // sort — picks the champion, and the assertion below becomes a coin flip.)
            var winnersIds = harness.Participants(tournamentId)
                .Where(p => p.Points == 3)
                .Select(p => p.Id!.Value)
                .ToHashSet();
            var round2 = harness.Matches(tournamentId).Where(m => m.RoundNumber == 2).ToList();
            var winnersMatch = round2.Single(m =>
                winnersIds.Contains(m.HomeParticipantId!.Value) && winnersIds.Contains(m.AwayParticipantId!.Value));
            var losersMatch = round2.Single(m => m.Id != winnersMatch.Id);

            await harness.NewService().UpdateMatchResult(new MatchResultDto
            {
                MatchId = winnersMatch.Id!.Value, TournamentId = tournamentId, HomeScore = 2, AwayScore = 0,
            });

            await harness.NewService().ApplyDoubleWalkover(losersMatch.Id!.Value);

            var tournament = harness.Tournament(tournamentId);
            Assert.That(tournament.Status, Is.EqualTo(TournamentStatus.Completed),
                "the final round's no-show still lets the Swiss conclude");

            var top = harness.Participants(tournamentId).OrderByDescending(p => p.Points).First();
            Assert.That(top.Points, Is.EqualTo(6), "the played winner stands alone at the top");
            Assert.That(tournament.WinnerUserId, Is.EqualTo(top.UserId), "winner from the standings");
        }

        [Test]
        public async Task LeagueNoShow_OnLastFixture_CompletesTheLeague()
        {
            var harness = new BracketTestHarness(useSqlite: true);
            var tournamentId = await harness.SeedSoloTournamentAsync(TournamentFormat.League, 4);
            await harness.NewService().GenerateLeagueTournament(tournamentId);

            // Each round's last open fixture is closed as a no-show, the rest get real results —
            // the loop naturally re-checks after each action since walkovers unlock rounds too.
            for (int guard = 0; guard < 50; guard++)
            {
                var open = harness.Matches(tournamentId)
                    .Where(m => m.Status != MatchStatus.Completed && m.Status != MatchStatus.NoShow
                        && (m.RoundOpenAt == null || m.RoundOpenAt <= DateTime.UtcNow))
                    .ToList();

                if (open.Count == 0) break;

                if (open.Count == 1)
                {
                    await harness.NewService().ApplyDoubleWalkover(open[0].Id!.Value);
                    continue;
                }

                await harness.NewService().UpdateMatchResult(new MatchResultDto
                {
                    MatchId = open[0].Id!.Value, TournamentId = tournamentId, HomeScore = 2, AwayScore = 1,
                });
            }

            var tournament = harness.Tournament(tournamentId);
            Assert.That(tournament.Status, Is.EqualTo(TournamentStatus.Completed),
                "a no-show fixture doesn't block league completion");

            var standings = await harness.NewService().GetLeagueStandings(tournamentId);
            Assert.That(tournament.WinnerUserId, Is.EqualTo(standings.First().UserId));
        }

        [Test]
        public async Task GroupNoShow_StillLetsTheGroupFinish_AndTheKnockoutDraw()
        {
            var harness = new BracketTestHarness(useSqlite: true);
            var tournamentId = await harness.SeedSoloTournamentAsync(TournamentFormat.GroupStageWithKnockout, 8);
            await harness.NewService().GenerateGroupStageWithKnockout(tournamentId, numberOfGroups: 2, qualifiersPerGroup: 2);

            var groupStage = harness.Stages(tournamentId).Single(s => s.Type == StageType.GroupStage);
            var knockoutStage = harness.Stages(tournamentId).Single(s => s.Type == StageType.SingleEliminationBracket);

            bool noShowApplied = false;
            for (int guard = 0; guard < 100; guard++)
            {
                var playable = harness.Matches(tournamentId)
                    .FirstOrDefault(m => m.TournamentStageId == groupStage.Id
                        && m.Status != MatchStatus.Completed && m.Status != MatchStatus.NoShow
                        && (m.RoundOpenAt == null || m.RoundOpenAt <= DateTime.UtcNow));
                if (playable == null) break;

                if (!noShowApplied)
                {
                    // First open fixture is closed as a no-show; the rest get real results.
                    await harness.NewService().ApplyDoubleWalkover(playable.Id!.Value);
                    noShowApplied = true;
                    continue;
                }

                await harness.NewService().UpdateMatchResult(new MatchResultDto
                {
                    MatchId = playable.Id!.Value, TournamentId = tournamentId, HomeScore = 2, AwayScore = 1,
                });
            }

            Assert.That(noShowApplied, Is.True, "precondition: one group fixture closed as no-show");
            Assert.That(harness.Matches(tournamentId).Any(m => m.TournamentStageId == knockoutStage.Id),
                Is.True, "the knockout was drawn despite the no-show fixture");
        }

        [Test]
        public async Task NoShow_CanBeUndone_ByDeletingTheResult()
        {
            var harness = new BracketTestHarness(useSqlite: true);
            var tournamentId = await harness.SeedSoloTournamentAsync(TournamentFormat.Swiss, 4);
            await harness.NewService().GenerateSwissTournament(tournamentId);

            var match = harness.Matches(tournamentId).First(m => m.RoundNumber == 1);
            await harness.NewService().ApplyDoubleWalkover(match.Id!.Value);
            Assert.That(harness.Match(match.Id!.Value).Status, Is.EqualTo(MatchStatus.NoShow));

            await harness.NewService().RevertMatchResult(match.Id!.Value);

            var reopened = harness.Match(match.Id!.Value);
            Assert.That(reopened.Status, Is.EqualTo(MatchStatus.Scheduled), "the mistaken no-show is reopened");
        }

        [Test]
        public async Task NoShow_CanBeOverwritten_ByALateRealResult()
        {
            var harness = new BracketTestHarness(useSqlite: true);
            var tournamentId = await harness.SeedSoloTournamentAsync(TournamentFormat.Swiss, 4);
            await harness.NewService().GenerateSwissTournament(tournamentId);

            var match = harness.Matches(tournamentId).First(m => m.RoundNumber == 1);
            await harness.NewService().ApplyDoubleWalkover(match.Id!.Value);

            // The pair played after all — the admin enters the real score over the no-show.
            await harness.NewService().UpdateMatchResult(new MatchResultDto
            {
                MatchId = match.Id!.Value, TournamentId = tournamentId, HomeScore = 3, AwayScore = 1,
            });

            var reported = harness.Match(match.Id!.Value);
            Assert.That(reported.Status, Is.EqualTo(MatchStatus.Completed), "real result replaced the no-show");
            Assert.That(reported.WinnerParticipantId, Is.EqualTo(match.HomeParticipantId));

            var home = harness.Participants(tournamentId).Single(p => p.Id == match.HomeParticipantId);
            Assert.That(home.Points, Is.EqualTo(3), "the late result now counts in the standings");
        }

        [Test]
        public async Task NoShow_OnAlreadyClosedMatch_Throws()
        {
            var harness = new BracketTestHarness(useSqlite: true);
            var tournamentId = await harness.SeedSoloTournamentAsync(TournamentFormat.Swiss, 4);
            await harness.NewService().GenerateSwissTournament(tournamentId);

            var match = harness.Matches(tournamentId).First(m => m.RoundNumber == 1);
            await harness.NewService().ApplyDoubleWalkover(match.Id!.Value);

            Assert.That(async () => await harness.NewService().ApplyDoubleWalkover(match.Id!.Value),
                Throws.TypeOf<BusinessRuleException>().With.Message.Contains("no-show"));
        }

        [Test]
        public async Task PlayInMatch_RejectsDoubleWalkover()
        {
            var harness = new BracketTestHarness(useSqlite: true);
            var tournamentId = await harness.SeedSoloTournamentAsync(
                TournamentFormat.Swiss, 6,
                swissRoundsCount: 1, swissKnockoutQualifiers: 4, swissDirectQualifiers: 2);
            await harness.NewService().GenerateSwissTournament(tournamentId);

            foreach (var m in harness.Matches(tournamentId).Where(m => m.RoundNumber == 1).ToList())
            {
                await harness.NewService().UpdateMatchResult(new MatchResultDto
                {
                    MatchId = m.Id!.Value, TournamentId = tournamentId, HomeScore = 2, AwayScore = 0,
                });
            }

            var playInStage = harness.Stages(tournamentId).Single(s => s.Type == StageType.PlayIn);
            var playInMatch = harness.Matches(tournamentId).First(m => m.TournamentStageId == playInStage.Id);

            Assert.That(async () => await harness.NewService().ApplyDoubleWalkover(playInMatch.Id!.Value),
                Throws.TypeOf<BusinessRuleException>().With.Message.Contains("play-in"),
                "a voided play-in slot would leave the knockout short of a qualifier");
        }
    }
}
