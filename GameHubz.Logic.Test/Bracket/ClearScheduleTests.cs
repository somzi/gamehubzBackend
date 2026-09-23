using System;
using System.Linq;
using System.Threading.Tasks;

using NUnit.Framework;

using GameHubz.DataModels.Enums;
using GameHubz.Logic.Exceptions;

namespace GameHubz.Logic.Test.Bracket
{
    // Organizer cancels a confirmed kick-off (MatchService.ClearSchedule). The action exists because
    // two players sometimes agree a time they cannot keep; what it must NOT become is a way for one
    // of them to walk away from a time the other is holding. These tests pin who may call it, what
    // state it is allowed in, and that it clears BOTH sides' offered hours — leaving them behind
    // would let SetAvailability re-confirm the same overlap on the next touch of the picker.
    [TestFixture]
    internal sealed class ClearScheduleTests
    {
        [Test]
        public async Task ClearSchedule_AsOrganizer_UnschedulesAndWipesBothSidesAvailability()
        {
            var harness = new BracketTestHarness(useSqlite: true);
            var tid = await harness.SeedSoloTournamentAsync(TournamentFormat.League, 4);
            await harness.NewService().GenerateLeagueTournament(tid);

            var match = harness.Matches(tid).First(m => m.RoundNumber == 1);
            harness.MarkScheduled(match.Id!.Value, DateTime.UtcNow.AddDays(1));

            await harness.NewMatchServiceAsUser(BracketTestHarness.OwnerUserId, "Admin")
                .ClearSchedule(match.Id!.Value);

            var cleared = harness.Match(match.Id!.Value);
            Assert.That(cleared.Status, Is.EqualTo(MatchStatus.Pending), "back to the state before the two lists met");
            Assert.That(cleared.ScheduledStartTime, Is.Null);
            Assert.That(cleared.HomeSlotsJson, Is.Null, "null, not an empty array — readers treat null as 'never answered'");
            Assert.That(cleared.AwaySlotsJson, Is.Null);
            Assert.That(cleared.HomeSlotsSetOn, Is.Null);
            Assert.That(cleared.AwaySlotsSetOn, Is.Null);
        }

        [Test]
        public async Task ClearSchedule_IsRefusedToAPlayerOfTheMatch()
        {
            var harness = new BracketTestHarness(useSqlite: true);
            var tid = await harness.SeedSoloTournamentAsync(TournamentFormat.League, 4);
            await harness.NewService().GenerateLeagueTournament(tid);

            var match = harness.Matches(tid).First(m => m.RoundNumber == 1);
            var kickOff = DateTime.UtcNow.AddDays(1);
            harness.MarkScheduled(match.Id!.Value, kickOff);

            // A participant, not a manager — the case the guard exists for.
            var playerUserId = harness.ParticipantUserId(match.HomeParticipantId!.Value);
            await harness.DenyManageFor(playerUserId, tid);

            Assert.ThrowsAsync<BusinessRuleException>(async () =>
                await harness.NewMatchServiceAsUser(playerUserId).ClearSchedule(match.Id!.Value));

            var untouched = harness.Match(match.Id!.Value);
            Assert.That(untouched.Status, Is.EqualTo(MatchStatus.Scheduled));
            Assert.That(untouched.ScheduledStartTime, Is.Not.Null, "the agreed time survives a refused call");
            Assert.That(untouched.HomeSlotsJson, Is.Not.Null);
            Assert.That(untouched.AwaySlotsJson, Is.Not.Null);
        }

        [Test]
        public async Task ClearSchedule_IsRefusedOnAMatchThatWasNeverScheduled()
        {
            var harness = new BracketTestHarness(useSqlite: true);
            var tid = await harness.SeedSoloTournamentAsync(TournamentFormat.League, 4);
            await harness.NewService().GenerateLeagueTournament(tid);

            var match = harness.Matches(tid).First(m => m.RoundNumber == 1);
            Assert.That(match.Status, Is.EqualTo(MatchStatus.Pending), "precondition: nothing is scheduled yet");

            Assert.ThrowsAsync<BusinessRuleException>(async () =>
                await harness.NewMatchServiceAsUser(BracketTestHarness.OwnerUserId, "Admin")
                    .ClearSchedule(match.Id!.Value));
        }

        [Test]
        public async Task ClearSchedule_LeavesAPlayedMatchAlone()
        {
            var harness = new BracketTestHarness(useSqlite: true);
            var tid = await harness.SeedSoloTournamentAsync(TournamentFormat.League, 4);
            await harness.NewService().GenerateLeagueTournament(tid);

            var match = harness.Matches(tid).First(m => m.RoundNumber == 1);
            harness.MarkScheduled(match.Id!.Value, DateTime.UtcNow.AddDays(-1));

            await harness.NewService().UpdateMatchResult(new DataModels.Models.MatchResultDto
            {
                MatchId = match.Id!.Value, TournamentId = tid, HomeScore = 2, AwayScore = 0,
            });

            Assert.ThrowsAsync<BusinessRuleException>(async () =>
                await harness.NewMatchServiceAsUser(BracketTestHarness.OwnerUserId, "Admin")
                    .ClearSchedule(match.Id!.Value));

            // A completed match keeps ScheduledStartTime as the record of when it was played.
            var played = harness.Match(match.Id!.Value);
            Assert.That(played.Status, Is.EqualTo(MatchStatus.Completed));
            Assert.That(played.ScheduledStartTime, Is.Not.Null);
        }

        // The organizer is looking at the bracket when they cancel a time, and the bracket is served
        // from a five-minute cache. Without an eviction the board keeps the kick-off — and its ready
        // check — for the rest of that window, so the cancel looks like it never happened.
        [Test]
        public async Task ClearSchedule_EvictsTheCachedBracketSoTheBoardStopsShowingTheOldKickOff()
        {
            var harness = new BracketTestHarness(useSqlite: true);
            var tid = await harness.SeedSoloTournamentAsync(TournamentFormat.League, 4);
            await harness.NewService().GenerateLeagueTournament(tid);

            var match = harness.Matches(tid).First(m => m.RoundNumber == 1);
            harness.MarkScheduled(match.Id!.Value, DateTime.UtcNow.AddDays(1));

            // Warm the cache with the scheduled state — this is the payload the board is holding.
            var before = Card(await harness.NewService().GetTournamentStructure(tid), match.Id!.Value);
            Assert.That(before.Status, Is.EqualTo(MatchStatus.Scheduled), "precondition: the cached board says scheduled");
            Assert.That(before.StartTime, Is.Not.Null);

            await harness.NewMatchServiceAsUser(BracketTestHarness.OwnerUserId, "Admin")
                .ClearSchedule(match.Id!.Value);

            var after = Card(await harness.NewService().GetTournamentStructure(tid), match.Id!.Value);
            Assert.That(after.Status, Is.EqualTo(MatchStatus.Pending), "the re-read must not be served the stale entry");
            Assert.That(after.StartTime, Is.Null, "and the cancelled kick-off is gone from the card");
        }

        private static DataModels.Models.MatchStructureDto Card(
            DataModels.Models.TournamentStructureDto structure, Guid matchId)
            => structure.Stages.Single().Groups!.Single().Matches.Single(m => m.Id == matchId);
    }
}
