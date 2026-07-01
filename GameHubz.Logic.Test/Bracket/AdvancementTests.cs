using System.Linq;
using System.Threading.Tasks;

using NUnit.Framework;

using GameHubz.DataModels.Domain;
using GameHubz.DataModels.Enums;
using GameHubz.DataModels.Models;
using GameHubz.Logic.Exceptions;

namespace GameHubz.Logic.Test.Bracket
{
    // Advancement / result-processing runs on the SQLite harness: UpdateMatchResult takes a
    // per-tournament Postgres advisory lock (raw SQL, relational-only) that the in-memory provider
    // can't execute. SQLite gives a real relational provider; the lock functions are registered as no-ops.
    [TestFixture]
    internal sealed class AdvancementTests
    {
        [Test]
        public async Task ReportingResult_CompletesMatchAndAdvancesWinner()
        {
            var harness = new BracketTestHarness(useSqlite: true);
            var tournamentId = await harness.SeedSoloTournamentAsync(TournamentFormat.SingleElimination, 8);
            await harness.NewService().GenerateSingleEliminationBracket(tournamentId);

            var r1 = harness.Matches(tournamentId).First(m => m.RoundNumber == 1);

            await harness.NewService().UpdateMatchResult(new MatchResultDto
            {
                MatchId = r1.Id!.Value,
                TournamentId = tournamentId,
                HomeScore = 2,
                AwayScore = 1,
            });

            var completed = harness.Match(r1.Id!.Value);
            Assert.That(completed.Status, Is.EqualTo(MatchStatus.Completed));
            Assert.That(completed.WinnerParticipantId, Is.EqualTo(r1.HomeParticipantId), "higher score wins");
            Assert.That(completed.HomeUserScore, Is.EqualTo(2));
            Assert.That(completed.AwayUserScore, Is.EqualTo(1));

            // Winner is placed into the next match.
            var next = harness.Match(r1.NextMatchId!.Value);
            bool winnerPlaced = next.HomeParticipantId == r1.HomeParticipantId
                || next.AwayParticipantId == r1.HomeParticipantId;
            Assert.That(winnerPlaced, Is.True, "winner advanced into the next match slot");
        }

        [Test]
        public async Task BothFeeders_FillTheNextMatch()
        {
            var harness = new BracketTestHarness(useSqlite: true);
            var tournamentId = await harness.SeedSoloTournamentAsync(TournamentFormat.SingleElimination, 4);
            await harness.NewService().GenerateSingleEliminationBracket(tournamentId);

            // N=4: two semi-finals feed the one final.
            var semis = harness.Matches(tournamentId).Where(m => m.RoundNumber == 1).ToList();
            Assert.That(semis.Count, Is.EqualTo(2));

            foreach (var semi in semis)
            {
                await harness.NewService().UpdateMatchResult(new MatchResultDto
                {
                    MatchId = semi.Id!.Value,
                    TournamentId = tournamentId,
                    HomeScore = 3,
                    AwayScore = 0,
                });
            }

            var final = harness.Match(semis[0].NextMatchId!.Value);
            Assert.That(final.HomeParticipantId, Is.Not.Null, "final home slot filled");
            Assert.That(final.AwayParticipantId, Is.Not.Null, "final away slot filled");
        }

        [Test]
        public async Task RevertResult_ReopensMatchAndClearsNextSlot()
        {
            var harness = new BracketTestHarness(useSqlite: true);
            var tournamentId = await harness.SeedSoloTournamentAsync(TournamentFormat.SingleElimination, 4);
            await harness.NewService().GenerateSingleEliminationBracket(tournamentId);

            var semi = harness.Matches(tournamentId).First(m => m.RoundNumber == 1);
            var winnerId = semi.HomeParticipantId;

            await harness.NewService().UpdateMatchResult(new MatchResultDto
            {
                MatchId = semi.Id!.Value, TournamentId = tournamentId, HomeScore = 2, AwayScore = 1,
            });

            // Confirm it advanced, then revert.
            await harness.NewService().RevertMatchResult(semi.Id!.Value);

            var reverted = harness.Match(semi.Id!.Value);
            Assert.That(reverted.Status, Is.EqualTo(MatchStatus.Scheduled), "match reopened");
            Assert.That(reverted.WinnerParticipantId, Is.Null, "winner cleared");
            Assert.That(reverted.HomeUserScore, Is.Null, "score cleared");

            var final = harness.Match(semi.NextMatchId!.Value);
            bool slotCleared = final.HomeParticipantId != winnerId && final.AwayParticipantId != winnerId;
            Assert.That(slotCleared, Is.True, "winner removed from the next match");
        }

        [Test]
        public async Task RevertIsLocked_WhenDownstreamMatchProgressed()
        {
            var harness = new BracketTestHarness(useSqlite: true);
            var tournamentId = await harness.SeedSoloTournamentAsync(TournamentFormat.SingleElimination, 4);
            await harness.NewService().GenerateSingleEliminationBracket(tournamentId);

            var semis = harness.Matches(tournamentId).Where(m => m.RoundNumber == 1).ToList();
            foreach (var semi in semis)
            {
                await harness.NewService().UpdateMatchResult(new MatchResultDto
                {
                    MatchId = semi.Id!.Value, TournamentId = tournamentId, HomeScore = 2, AwayScore = 1,
                });
            }

            // Final now has both finalists; play it.
            var finalId = semis[0].NextMatchId!.Value;
            await harness.NewService().UpdateMatchResult(new MatchResultDto
            {
                MatchId = finalId, TournamentId = tournamentId, HomeScore = 1, AwayScore = 0,
            });

            // Reverting a semi-final is now locked — the final has progressed.
            Assert.That(async () => await harness.NewService().RevertMatchResult(semis[0].Id!.Value),
                Throws.Exception);
        }

        [Test]
        public async Task DrawInElimination_Throws()
        {
            var harness = new BracketTestHarness(useSqlite: true);
            var tournamentId = await harness.SeedSoloTournamentAsync(TournamentFormat.SingleElimination, 4);
            await harness.NewService().GenerateSingleEliminationBracket(tournamentId);

            var semi = harness.Matches(tournamentId).First(m => m.RoundNumber == 1);

            Assert.That(async () => await harness.NewService().UpdateMatchResult(new MatchResultDto
            {
                MatchId = semi.Id!.Value, TournamentId = tournamentId, HomeScore = 1, AwayScore = 1,
            }), Throws.Exception, "elimination needs a winner");
        }

        [Test]
        public async Task FullSingleEliminationPlaythrough_CompletesTournamentWithAChampion()
        {
            var harness = new BracketTestHarness(useSqlite: true);
            var tournamentId = await harness.SeedSoloTournamentAsync(TournamentFormat.SingleElimination, 8);
            await harness.NewService().GenerateSingleEliminationBracket(tournamentId);

            await PlayOutAllPendingSoloMatches(harness, tournamentId);

            var tournament = harness.Tournament(tournamentId);
            Assert.That(tournament.Status, Is.EqualTo(TournamentStatus.Completed), "tournament concluded");

            var final = harness.Matches(tournamentId).Single(m => m.Stage == MatchStage.Final);
            Assert.That(final.Status, Is.EqualTo(MatchStatus.Completed), "final played");
            Assert.That(final.WinnerParticipantId, Is.Not.Null, "champion decided");
        }

        [Test]
        public async Task LeagueStandings_ReflectReportedResults()
        {
            var harness = new BracketTestHarness(useSqlite: true);
            var tournamentId = await harness.SeedSoloTournamentAsync(TournamentFormat.League, 4);
            await harness.NewService().GenerateLeagueTournament(tournamentId);

            // Pick a match whose round is actually open: later league rounds are locked (RoundOpenAt
            // in the future) and Matches() has no ordering, so an unfiltered First() is nondeterministic
            // and can land on a locked round → "round is not open yet".
            var match = harness.Matches(tournamentId)
                .First(m => m.Stage == MatchStage.GroupStage
                    && (m.RoundOpenAt == null || m.RoundOpenAt <= System.DateTime.UtcNow));
            var homeId = match.HomeParticipantId!.Value;
            var awayId = match.AwayParticipantId!.Value;

            await harness.NewService().UpdateMatchResult(new MatchResultDto
            {
                MatchId = match.Id!.Value, TournamentId = tournamentId, HomeScore = 3, AwayScore = 1,
            });

            var standings = await harness.NewService().GetLeagueStandings(tournamentId);

            var home = standings.Single(s => s.ParticipantId == homeId);
            var away = standings.Single(s => s.ParticipantId == awayId);

            Assert.That(home.Points, Is.EqualTo(3), "win = 3 points");
            Assert.That(home.Wins, Is.EqualTo(1));
            Assert.That(home.GoalsFor, Is.EqualTo(3));
            Assert.That(home.GoalsAgainst, Is.EqualTo(1));
            Assert.That(away.Points, Is.EqualTo(0));
            Assert.That(away.Losses, Is.EqualTo(1));
            Assert.That(home.Position, Is.LessThan(away.Position), "winner ranks above loser");
        }

        [Test]
        public async Task GroupStageWithByes_SixQualifiers_DrawsBracketOfEightWithTwoByesAndCompletes()
        {
            var harness = new BracketTestHarness(useSqlite: true);
            var tournamentId = await harness.SeedSoloTournamentAsync(TournamentFormat.GroupStageWithKnockout, 8);

            // 2 groups of 4, top 3 advance -> 6 qualifiers. Not a power of two, so the knockout is
            // padded to a bracket of 8 with the two best seeds on a bye.
            await harness.NewService().GenerateGroupStageWithKnockout(tournamentId, numberOfGroups: 2, qualifiersPerGroup: 3);

            // Play out every match as its round opens (group rounds unlock progressively, then the
            // knockout is drawn and played). Respecting RoundOpenAt avoids "round is not open yet".
            for (int guard = 0; guard < 300; guard++)
            {
                var playable = harness.Matches(tournamentId)
                    .FirstOrDefault(m => m.Status != MatchStatus.Completed
                        && m.HomeParticipantId.HasValue && m.AwayParticipantId.HasValue
                        && (m.RoundOpenAt == null || m.RoundOpenAt <= System.DateTime.UtcNow));
                if (playable == null) break;

                await harness.NewService().UpdateMatchResult(new MatchResultDto
                {
                    MatchId = playable.Id!.Value, TournamentId = tournamentId, HomeScore = 2, AwayScore = 1,
                });
            }

            var knockoutStage = harness.Stages(tournamentId).Single(s => s.Type == StageType.SingleEliminationBracket);
            var firstRound = harness.Matches(tournamentId)
                .Where(m => m.TournamentStageId == knockoutStage.Id && m.RoundNumber == 1)
                .ToList();

            Assert.That(firstRound.Count, Is.EqualTo(4), "bracket of 8 has four first-round slots");
            var byes = firstRound.Count(m => m.HomeParticipantId.HasValue ^ m.AwayParticipantId.HasValue);
            Assert.That(byes, Is.EqualTo(2), "6 qualifiers in a bracket of 8 -> exactly 2 byes");

            var tournament = harness.Tournament(tournamentId);
            Assert.That(tournament.Status, Is.EqualTo(TournamentStatus.Completed), "tournament played to a champion");
        }

        [Test]
        public async Task ResetKnockoutStage_TearsDownBracket_ThenRedrawsFromGroups()
        {
            var harness = new BracketTestHarness(useSqlite: true);
            var tournamentId = await harness.SeedSoloTournamentAsync(TournamentFormat.GroupStageWithKnockout, 8);
            await harness.NewService().GenerateGroupStageWithKnockout(tournamentId, numberOfGroups: 2, qualifiersPerGroup: 2);

            var groupStage = harness.Stages(tournamentId).Single(s => s.Type == StageType.GroupStage);

            // Play only the groups — auto-advance draws the knockout when the last group match completes.
            // We deliberately stop before touching the knockout: reset is only legal while nothing there
            // has been played, which is the flow this test guards.
            for (int guard = 0; guard < 300; guard++)
            {
                var playable = harness.Matches(tournamentId)
                    .FirstOrDefault(m => m.TournamentStageId == groupStage.Id
                        && m.Status != MatchStatus.Completed
                        && m.HomeParticipantId.HasValue && m.AwayParticipantId.HasValue
                        && (m.RoundOpenAt == null || m.RoundOpenAt <= System.DateTime.UtcNow));
                if (playable == null) break;
                await harness.NewService().UpdateMatchResult(new MatchResultDto
                {
                    MatchId = playable.Id!.Value, TournamentId = tournamentId, HomeScore = 2, AwayScore = 1,
                });
            }

            var knockoutStage = harness.Stages(tournamentId).Single(s => s.Type == StageType.SingleEliminationBracket);
            int drawnCount = harness.Matches(tournamentId).Count(m => m.TournamentStageId == knockoutStage.Id);
            Assert.That(drawnCount, Is.GreaterThan(0), "bracket was auto-drawn after the groups completed");

            // Nothing in the knockout has been played yet — reset should tear it down cleanly.
            await harness.NewService().ResetKnockoutStage(tournamentId);
            Assert.That(harness.Matches(tournamentId).Any(m => m.TournamentStageId == knockoutStage.Id), Is.False,
                "knockout matches cleared");
            Assert.That(harness.Tournament(tournamentId).Status, Is.EqualTo(TournamentStatus.InProgress),
                "tournament stays in-progress (no champion was crowned)");

            // A manual draw rebuilds the same-shape bracket from the unchanged standings.
            await harness.NewService().DrawKnockoutFromGroups(tournamentId);
            int redrawnCount = harness.Matches(tournamentId).Count(m => m.TournamentStageId == knockoutStage.Id);
            Assert.That(redrawnCount, Is.EqualTo(drawnCount), "bracket redrawn with the same number of fixtures");
        }

        [Test]
        public async Task ResetKnockoutStage_ThrowsWhenAnyKnockoutMatchPlayed()
        {
            var harness = new BracketTestHarness(useSqlite: true);
            var tournamentId = await harness.SeedSoloTournamentAsync(TournamentFormat.GroupStageWithKnockout, 8);
            await harness.NewService().GenerateGroupStageWithKnockout(tournamentId, numberOfGroups: 2, qualifiersPerGroup: 2);

            var groupStage = harness.Stages(tournamentId).Single(s => s.Type == StageType.GroupStage);
            var knockoutStage = harness.Stages(tournamentId).Single(s => s.Type == StageType.SingleEliminationBracket);

            // Groups first — that's what triggers the auto-draw of the knockout.
            for (int guard = 0; guard < 300; guard++)
            {
                var playable = harness.Matches(tournamentId)
                    .FirstOrDefault(m => m.TournamentStageId == groupStage.Id
                        && m.Status != MatchStatus.Completed
                        && m.HomeParticipantId.HasValue && m.AwayParticipantId.HasValue
                        && (m.RoundOpenAt == null || m.RoundOpenAt <= System.DateTime.UtcNow));
                if (playable == null) break;
                await harness.NewService().UpdateMatchResult(new MatchResultDto
                {
                    MatchId = playable.Id!.Value, TournamentId = tournamentId, HomeScore = 2, AwayScore = 1,
                });
            }

            // Play one real 2-sided knockout fixture. That alone must lock the bracket against reset —
            // the organiser has to revert individual results first if they want to redraw.
            var playedKnockout = harness.Matches(tournamentId)
                .First(m => m.TournamentStageId == knockoutStage.Id
                    && m.Status != MatchStatus.Completed
                    && m.HomeParticipantId.HasValue && m.AwayParticipantId.HasValue);
            await harness.NewService().UpdateMatchResult(new MatchResultDto
            {
                MatchId = playedKnockout.Id!.Value, TournamentId = tournamentId, HomeScore = 2, AwayScore = 1,
            });

            var ex = Assert.ThrowsAsync<BusinessRuleException>(
                () => harness.NewService().ResetKnockoutStage(tournamentId));
            Assert.That(ex!.Message, Does.Contain("already been played"));

            // The refused reset must be a full no-op: bracket intact, nothing torn down.
            Assert.That(harness.Matches(tournamentId).Any(m => m.TournamentStageId == knockoutStage.Id), Is.True,
                "knockout not torn down after refused reset");
        }

        [Test]
        public async Task SwapBracketParticipants_ExchangesTwoFirstRoundSlots()
        {
            var harness = new BracketTestHarness(useSqlite: true);
            var tournamentId = await harness.SeedSoloTournamentAsync(TournamentFormat.GroupStageWithKnockout, 8);
            await harness.NewService().GenerateGroupStageWithKnockout(tournamentId, numberOfGroups: 2, qualifiersPerGroup: 2);

            var groupStage = harness.Stages(tournamentId).Single(s => s.Type == StageType.GroupStage);

            // Play out ONLY the group stage so the knockout auto-draws but stays unplayed.
            for (int guard = 0; guard < 300; guard++)
            {
                var playable = harness.Matches(tournamentId)
                    .FirstOrDefault(m => m.TournamentStageId == groupStage.Id
                        && m.Status != MatchStatus.Completed
                        && m.HomeParticipantId.HasValue && m.AwayParticipantId.HasValue
                        && (m.RoundOpenAt == null || m.RoundOpenAt <= System.DateTime.UtcNow));
                if (playable == null) break;
                await harness.NewService().UpdateMatchResult(new MatchResultDto
                {
                    MatchId = playable.Id!.Value, TournamentId = tournamentId, HomeScore = 2, AwayScore = 1,
                });
            }

            var knockoutStage = harness.Stages(tournamentId).Single(s => s.Type == StageType.SingleEliminationBracket);
            var firstRound = harness.Matches(tournamentId)
                .Where(m => m.TournamentStageId == knockoutStage.Id && m.RoundNumber == 1)
                .OrderBy(m => m.MatchOrder).ToList();
            Assert.That(firstRound.Count, Is.EqualTo(2), "bracket of 4 has two first-round matches");

            var a = firstRound[0].HomeParticipantId!.Value;
            var b = firstRound[1].HomeParticipantId!.Value;

            await harness.NewService().SwapBracketParticipants(tournamentId, a, b);

            var after = harness.Matches(tournamentId)
                .Where(m => m.TournamentStageId == knockoutStage.Id && m.RoundNumber == 1)
                .OrderBy(m => m.MatchOrder).ToList();
            Assert.That(after[0].HomeParticipantId, Is.EqualTo(b), "B took A's slot");
            Assert.That(after[1].HomeParticipantId, Is.EqualTo(a), "A took B's slot");
        }

        [Test]
        public async Task SwapBracketParticipants_CanSwapAByeTeamViaRegenerate()
        {
            var harness = new BracketTestHarness(useSqlite: true);
            var tournamentId = await harness.SeedSoloTournamentAsync(TournamentFormat.GroupStageWithKnockout, 8);
            // 6 qualifiers -> bracket of 8 with two byes on the top seeds.
            await harness.NewService().GenerateGroupStageWithKnockout(tournamentId, numberOfGroups: 2, qualifiersPerGroup: 3);

            var groupStage = harness.Stages(tournamentId).Single(s => s.Type == StageType.GroupStage);
            for (int guard = 0; guard < 300; guard++)
            {
                var playable = harness.Matches(tournamentId)
                    .FirstOrDefault(m => m.TournamentStageId == groupStage.Id
                        && m.Status != MatchStatus.Completed
                        && m.HomeParticipantId.HasValue && m.AwayParticipantId.HasValue
                        && (m.RoundOpenAt == null || m.RoundOpenAt <= System.DateTime.UtcNow));
                if (playable == null) break;
                await harness.NewService().UpdateMatchResult(new MatchResultDto
                {
                    MatchId = playable.Id!.Value, TournamentId = tournamentId, HomeScore = 2, AwayScore = 1,
                });
            }

            var knockoutStage = harness.Stages(tournamentId).Single(s => s.Type == StageType.SingleEliminationBracket);
            System.Func<System.Collections.Generic.List<GameHubz.DataModels.Domain.MatchEntity>> r1 = () => harness.Matches(tournamentId)
                .Where(m => m.TournamentStageId == knockoutStage.Id && m.RoundNumber == 1).ToList();

            var byeMatch = r1().First(m => m.HomeParticipantId.HasValue ^ m.AwayParticipantId.HasValue);
            var byeId = (byeMatch.HomeParticipantId ?? byeMatch.AwayParticipantId)!.Value;
            var realId = r1().First(m => m.HomeParticipantId.HasValue && m.AwayParticipantId.HasValue).HomeParticipantId!.Value;

            await harness.NewService().SwapBracketParticipants(tournamentId, byeId, realId);

            var byeIdsAfter = r1()
                .Where(m => m.HomeParticipantId.HasValue ^ m.AwayParticipantId.HasValue)
                .Select(m => (m.HomeParticipantId ?? m.AwayParticipantId)!.Value)
                .ToHashSet();
            Assert.That(byeIdsAfter, Does.Contain(realId), "the formerly-playing team now holds the bye");
            Assert.That(byeIdsAfter, Does.Not.Contain(byeId), "the formerly-bye team now plays a first-round match");
        }

        /// <summary>
        /// Reports a 2-1 home win on every playable solo match (Pending with both participants), round
        /// after round, until the bracket is fully played. Each report uses a fresh service/context.
        /// </summary>
        private static async Task PlayOutAllPendingSoloMatches(BracketTestHarness harness, System.Guid tournamentId)
        {
            for (int guard = 0; guard < 100; guard++)
            {
                var playable = harness.Matches(tournamentId)
                    .FirstOrDefault(m => m.Status != MatchStatus.Completed
                        && m.HomeParticipantId.HasValue && m.AwayParticipantId.HasValue);

                if (playable == null) break;

                await harness.NewService().UpdateMatchResult(new MatchResultDto
                {
                    MatchId = playable.Id!.Value,
                    TournamentId = tournamentId,
                    HomeScore = 2,
                    AwayScore = 1,
                });
            }
        }
    }
}
