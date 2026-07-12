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
    // Swiss round-to-round advancement: next-round pairing (rematch avoidance), the odd-pool bye
    // (free win + rotation — regression for the tracked-entity DetachById bug that used to stop
    // the next round from ever pairing), final-round completion for pure Swiss, and the
    // Swiss → knockout / Swiss → play-in → knockout hand-offs. SQLite harness (advancement path).
    [TestFixture]
    internal sealed class SwissAdvancementTests
    {
        [Test]
        public async Task CompletingRound_PairsNextRound_WithoutRematches()
        {
            var harness = new BracketTestHarness(useSqlite: true);
            var tournamentId = await harness.SeedSoloTournamentAsync(TournamentFormat.Swiss, 4);
            await harness.NewService().GenerateSwissTournament(tournamentId);

            var round1 = harness.Matches(tournamentId).Where(m => m.RoundNumber == 1).ToList();
            Assert.That(round1.Count, Is.EqualTo(2), "4 players -> 2 first-round matches");

            foreach (var m in round1)
            {
                await harness.NewService().UpdateMatchResult(new MatchResultDto
                {
                    MatchId = m.Id!.Value, TournamentId = tournamentId, HomeScore = 2, AwayScore = 0,
                });
            }

            var round2 = harness.Matches(tournamentId).Where(m => m.RoundNumber == 2).ToList();
            Assert.That(round2.Count, Is.EqualTo(2), "next round paired as soon as the round completed");

            var round1Pairs = round1
                .Select(m => (A: m.HomeParticipantId!.Value, B: m.AwayParticipantId!.Value))
                .ToList();
            foreach (var m in round2)
            {
                bool rematch = round1Pairs.Any(p =>
                    (p.A == m.HomeParticipantId && p.B == m.AwayParticipantId)
                    || (p.A == m.AwayParticipantId && p.B == m.HomeParticipantId));
                Assert.That(rematch, Is.False, "round 2 must not repeat a round-1 pairing");
            }

            // Winners (3 pts) meet each other in round 2 — that's the Swiss invariant the pairing
            // exists for. The two round-1 winners are the home sides (2:0 above).
            var winners = round1.Select(m => m.HomeParticipantId!.Value).ToHashSet();
            bool winnersPaired = round2.Any(m =>
                winners.Contains(m.HomeParticipantId!.Value) && winners.Contains(m.AwayParticipantId!.Value));
            Assert.That(winnersPaired, Is.True, "same-score players are paired together");
        }

        [Test]
        public async Task OddPool_GrantsByeAsFreeWin_AndNextRoundRotatesBye()
        {
            var harness = new BracketTestHarness(useSqlite: true);
            // 3 rounds so completing round 1 pairs a round 2 (default for 5 would be 3 anyway).
            var tournamentId = await harness.SeedSoloTournamentAsync(
                TournamentFormat.Swiss, 5, swissRoundsCount: 3);
            await harness.NewService().GenerateSwissTournament(tournamentId);

            var round1 = harness.Matches(tournamentId).Where(m => m.RoundNumber == 1).ToList();
            var round1Bye = round1.Single(m => m.AwayParticipantId == null);
            var round1Real = round1.Where(m => m.AwayParticipantId != null).ToList();

            Assert.That(round1.Count, Is.EqualTo(3), "2 real matches + 1 bye row");
            Assert.That(round1Bye.Status, Is.EqualTo(MatchStatus.Completed), "bye is pre-completed");
            Assert.That(round1Bye.WinnerParticipantId, Is.EqualTo(round1Bye.HomeParticipantId), "bye is a free win");

            var byeHolder = harness.Participants(tournamentId)
                .Single(p => p.Id == round1Bye.HomeParticipantId);
            Assert.That(byeHolder.Points, Is.EqualTo(3), "bye banks 3 points immediately");
            Assert.That(byeHolder.Wins, Is.EqualTo(1), "bye counts as a win");

            // Completing the two real matches must pair round 2. This is the regression path for
            // the tracked-participant conflict: before the DetachById fix the bye credit threw an
            // EF identity conflict here and round 2 was never created.
            foreach (var m in round1Real)
            {
                await harness.NewService().UpdateMatchResult(new MatchResultDto
                {
                    MatchId = m.Id!.Value, TournamentId = tournamentId, HomeScore = 2, AwayScore = 1,
                });
            }

            var round2 = harness.Matches(tournamentId).Where(m => m.RoundNumber == 2).ToList();
            Assert.That(round2.Count, Is.EqualTo(3), "round 2 paired: 2 real matches + 1 bye");

            var round2Bye = round2.Single(m => m.AwayParticipantId == null);
            Assert.That(round2Bye.HomeParticipantId, Is.Not.EqualTo(round1Bye.HomeParticipantId),
                "the bye rotates — fewest-byes rule keeps round 1's bye holder playing");
        }

        [Test]
        public async Task ByeMatch_CannotBeReported()
        {
            var harness = new BracketTestHarness(useSqlite: true);
            var tournamentId = await harness.SeedSoloTournamentAsync(TournamentFormat.Swiss, 5);
            await harness.NewService().GenerateSwissTournament(tournamentId);

            var bye = harness.Matches(tournamentId).Single(m => m.RoundNumber == 1 && m.AwayParticipantId == null);

            Assert.That(async () => await harness.NewService().UpdateMatchResult(new MatchResultDto
            {
                MatchId = bye.Id!.Value, TournamentId = tournamentId, HomeScore = 2, AwayScore = 0,
            }), Throws.TypeOf<BusinessRuleException>(), "a bye row has no opponent and takes no result");
        }

        [Test]
        public async Task PureSwiss_FinalRound_CompletesTournament_WinnerTopsStandings()
        {
            var harness = new BracketTestHarness(useSqlite: true);
            var tournamentId = await harness.SeedSoloTournamentAsync(TournamentFormat.Swiss, 4);
            await harness.NewService().GenerateSwissTournament(tournamentId);

            // 4 players -> 2 rounds; rounds are paired on the fly, so keep reporting until no
            // playable match remains.
            await PlayOutSwissAsync(harness, tournamentId);

            var tournament = harness.Tournament(tournamentId);
            Assert.That(tournament.Status, Is.EqualTo(TournamentStatus.Completed), "final round completed the Swiss");

            // Home always won 2:0, so exactly one player finished 2-0 (the round-2 winners' match).
            var top = harness.Participants(tournamentId).OrderByDescending(p => p.Points).First();
            Assert.That(top.Points, Is.EqualTo(6), "one player wins both rounds");
            Assert.That(tournament.WinnerUserId, Is.EqualTo(top.UserId), "winner comes from the standings");
        }

        [Test]
        public async Task SwissWithDirectKnockout_FreezesSeeds_AndDrawsBracketOfWinners()
        {
            var harness = new BracketTestHarness(useSqlite: true);
            // One Swiss round, then a knockout of 4 with all four berths direct (no play-in).
            var tournamentId = await harness.SeedSoloTournamentAsync(
                TournamentFormat.Swiss, 8,
                swissRoundsCount: 1, swissKnockoutQualifiers: 4, swissDirectQualifiers: 4);
            await harness.NewService().GenerateSwissTournament(tournamentId);

            var swissRound = harness.Matches(tournamentId).Where(m => m.RoundNumber == 1).ToList();
            Assert.That(swissRound.Count, Is.EqualTo(4), "8 players -> 4 Swiss matches");

            foreach (var m in swissRound)
            {
                await harness.NewService().UpdateMatchResult(new MatchResultDto
                {
                    MatchId = m.Id!.Value, TournamentId = tournamentId, HomeScore = 2, AwayScore = 0,
                });
            }

            // Standings ranks froze into Seed (1..8, unique).
            var seeds = harness.Participants(tournamentId).Select(p => p.Seed).ToList();
            Assert.That(seeds, Is.EquivalentTo(Enumerable.Range(1, 8)), "final standings frozen into Seed");

            // The knockout bracket was drawn from the four 3-point winners.
            var knockoutStage = harness.Stages(tournamentId).Single(s => s.Type == StageType.SingleEliminationBracket);
            var knockoutFirstRound = harness.Matches(tournamentId)
                .Where(m => m.TournamentStageId == knockoutStage.Id && m.RoundNumber == 1)
                .ToList();
            Assert.That(knockoutFirstRound.Count, Is.EqualTo(2), "knockout of 4 -> 2 semi-finals");

            var winners = swissRound.Select(m => m.HomeParticipantId!.Value).ToHashSet();
            var seededIntoKnockout = knockoutFirstRound
                .SelectMany(m => new[] { m.HomeParticipantId, m.AwayParticipantId })
                .Select(id => id!.Value)
                .ToHashSet();
            Assert.That(seededIntoKnockout, Is.EquivalentTo(winners), "top 4 = the four Swiss winners");
        }

        [Test]
        public async Task SwissWithPlayIn_PlayInWinnersJoinDirectSeedsInKnockout()
        {
            var harness = new BracketTestHarness(useSqlite: true);
            // 6 players, 1 Swiss round; knockout of 4 = 2 direct berths + 2 play-in slots
            // (ranks 3..6 pair off best-vs-worst).
            var tournamentId = await harness.SeedSoloTournamentAsync(
                TournamentFormat.Swiss, 6,
                swissRoundsCount: 1, swissKnockoutQualifiers: 4, swissDirectQualifiers: 2);
            await harness.NewService().GenerateSwissTournament(tournamentId);

            var playInStage = harness.Stages(tournamentId).Single(s => s.Type == StageType.PlayIn);
            var knockoutStage = harness.Stages(tournamentId).Single(s => s.Type == StageType.SingleEliminationBracket);

            var swissRound = harness.Matches(tournamentId).Where(m => m.RoundNumber == 1).ToList();
            Assert.That(swissRound.Count, Is.EqualTo(3), "6 players -> 3 Swiss matches");

            foreach (var m in swissRound)
            {
                await harness.NewService().UpdateMatchResult(new MatchResultDto
                {
                    MatchId = m.Id!.Value, TournamentId = tournamentId, HomeScore = 2, AwayScore = 0,
                });
            }

            var playInMatches = harness.Matches(tournamentId)
                .Where(m => m.TournamentStageId == playInStage.Id)
                .ToList();
            Assert.That(playInMatches.Count, Is.EqualTo(2), "2 open knockout slots -> 2 play-in matches");
            Assert.That(playInMatches.All(m => m.HomeParticipantId.HasValue && m.AwayParticipantId.HasValue),
                Is.True, "play-in pairs are fully seeded");
            Assert.That(harness.Matches(tournamentId).Any(m => m.TournamentStageId == knockoutStage.Id),
                Is.False, "knockout waits for the play-in");

            foreach (var m in playInMatches)
            {
                await harness.NewService().UpdateMatchResult(new MatchResultDto
                {
                    MatchId = m.Id!.Value, TournamentId = tournamentId, HomeScore = 1, AwayScore = 0,
                });
            }

            var knockoutFirstRound = harness.Matches(tournamentId)
                .Where(m => m.TournamentStageId == knockoutStage.Id && m.RoundNumber == 1)
                .ToList();
            Assert.That(knockoutFirstRound.Count, Is.EqualTo(2), "knockout of 4 drawn after the play-in");

            var playInWinners = playInMatches.Select(m => m.HomeParticipantId!.Value).ToHashSet();
            var knockoutField = knockoutFirstRound
                .SelectMany(m => new[] { m.HomeParticipantId, m.AwayParticipantId })
                .Select(id => id!.Value)
                .ToHashSet();
            Assert.That(knockoutField.Count, Is.EqualTo(4), "four distinct qualifiers");
            Assert.That(playInWinners.IsSubsetOf(knockoutField), Is.True, "both play-in winners qualified");

            var directSeeds = harness.Participants(tournamentId)
                .Where(p => p.Seed <= 2)
                .Select(p => p.Id!.Value)
                .ToHashSet();
            Assert.That(directSeeds.IsSubsetOf(knockoutField), Is.True, "ranks 1-2 entered directly");
        }

        // Reports every playable (both sides present, round open) match home-win until none remain.
        // Swiss creates rounds on the fly, so this naturally walks round by round.
        private static async Task PlayOutSwissAsync(BracketTestHarness harness, Guid tournamentId)
        {
            for (int guard = 0; guard < 100; guard++)
            {
                var playable = harness.Matches(tournamentId)
                    .FirstOrDefault(m => m.Status != MatchStatus.Completed
                        && m.HomeParticipantId.HasValue && m.AwayParticipantId.HasValue
                        && (m.RoundOpenAt == null || m.RoundOpenAt <= DateTime.UtcNow));
                if (playable == null) break;

                await harness.NewService().UpdateMatchResult(new MatchResultDto
                {
                    MatchId = playable.Id!.Value, TournamentId = tournamentId, HomeScore = 2, AwayScore = 0,
                });
            }
        }
    }
}
