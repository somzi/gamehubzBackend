using System;
using System.Data.Common;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GameHubz.DataModels.Domain;
using GameHubz.DataModels.Enums;
using GameHubz.Logic.Exceptions;
using GameHubz.Logic.Services;
using GameHubz.Logic.Test.Factories;
using Microsoft.EntityFrameworkCore.Diagnostics;
using NUnit.Framework;

namespace GameHubz.Logic.Test.Bracket
{
    [TestFixture]
    internal sealed class MatchSchedulingSwapTests
    {
        public enum WriteKind { Availability, SetScheduled, ClearSchedule, CheckIn }

        // Pause a real scheduling request after its first read, at the acquisition of the
        // tournament lock. A separate request commits a real swap before this one resumes.
        // SQLite's advisory locks are no-ops, so the interceptor supplies the exact interleaving;
        // reads, writes and the swap transaction still use the real repositories.
        [TestCase(WriteKind.Availability, false)]
        [TestCase(WriteKind.SetScheduled, false)]
        [TestCase(WriteKind.ClearSchedule, false)]
        [TestCase(WriteKind.CheckIn, false)]
        [TestCase(WriteKind.Availability, true)]
        [TestCase(WriteKind.SetScheduled, true)]
        [TestCase(WriteKind.ClearSchedule, true)]
        [TestCase(WriteKind.CheckIn, true)]
        public async Task RequestWaitingForSwap_CannotWriteIntoTheNewPairing(WriteKind kind, bool flipSides)
        {
            var (harness, tid, fixture, other) = await SetUpBracket();
            var gate = new LockGate();
            var service = ServiceFor(harness, fixture, kind, gate);
            var pending = Write(service, fixture.Id!.Value, kind);

            string afterSwap;
            try
            {
                await AwaitLock(pending, gate);
                await harness.NewService().SwapBracketParticipants(tid, fixture.HomeParticipantId!.Value,
                    (flipSides ? fixture.AwayParticipantId : other.HomeParticipantId)!.Value);
                afterSwap = Snapshot(harness, tid);
            }
            finally
            {
                gate.Resume.TrySetResult();
            }

            var error = Assert.ThrowsAsync<BusinessRuleException>(async () =>
                await pending.WaitAsync(TimeSpan.FromSeconds(10)));
            var localization = new LocalizationServiceFactory().CreateService();
            Assert.Multiple(() =>
            {
                Assert.That(error!.Message, Is.EqualTo(localization["BusinessRule.MatchParticipantsChanged"]));
                Assert.That(Snapshot(harness, tid), Is.EqualTo(afterSwap),
                    "the old request must not restore players, schedule, availability or check-in state");
                Assert.That(gate.Releases, Is.EqualTo(1), "a rejected request must release the lock");
                Assert.That(gate.WritesOutsideLock, Is.Zero);
            });
        }

        [TestCase(WriteKind.Availability)]
        [TestCase(WriteKind.SetScheduled)]
        [TestCase(WriteKind.ClearSchedule)]
        [TestCase(WriteKind.CheckIn)]
        public async Task TeamSwapRebuiltTheGame_OldRequestCannotWriteOrRecreateIt(WriteKind kind)
        {
            var (harness, tid, fixture, other) = await SetUpBracket(team: true);
            var gate = new LockGate();
            var pending = Write(ServiceFor(harness, fixture, kind, gate), fixture.Id!.Value, kind);
            string afterSwap;
            try
            {
                await AwaitLock(pending, gate);
                await harness.NewService().SwapBracketParticipants(tid,
                    fixture.HomeParticipantId!.Value, other.HomeParticipantId!.Value);
                afterSwap = Snapshot(harness, tid);
            }
            finally
            {
                gate.Resume.TrySetResult();
            }

            var error = Assert.ThrowsAsync<BusinessRuleException>(async () =>
                await pending.WaitAsync(TimeSpan.FromSeconds(10)));
            var localization = new LocalizationServiceFactory().CreateService();
            Assert.That(error!.Message, Is.EqualTo(localization["BusinessRule.MatchNotFound"]));
            Assert.That(harness.Matches(tid).Any(m => m.Id == fixture.Id), Is.False);
            Assert.That(Snapshot(harness, tid), Is.EqualTo(afterSwap));
            Assert.That(gate.Releases, Is.EqualTo(1));
        }

        [Test]
        public async Task TeamNominationChangedWhileWaiting_OldPlayersCheckInIsRejected()
        {
            var (harness, tid, fixture, _) = await SetUpBracket(team: true);
            var gate = new LockGate();
            var pending = Write(ServiceFor(harness, fixture, WriteKind.CheckIn, gate), fixture.Id!.Value, WriteKind.CheckIn);
            var replacement = Guid.NewGuid();
            try
            {
                await AwaitLock(pending, gate);
                using var context = harness.ReadContext();
                context.Set<MatchEntity>().Single(m => m.Id == fixture.Id).HomeUserId = replacement;
                context.SaveChanges();
            }
            finally
            {
                gate.Resume.TrySetResult();
            }

            var error = Assert.ThrowsAsync<BusinessRuleException>(async () =>
                await pending.WaitAsync(TimeSpan.FromSeconds(10)));
            var localization = new LocalizationServiceFactory().CreateService();
            Assert.That(error!.Message, Is.EqualTo(localization["BusinessRule.MatchParticipantsChanged"]));
            Assert.That(harness.Match(fixture.Id!.Value).HomeUserId, Is.EqualTo(replacement));
            Assert.That(harness.Match(fixture.Id!.Value).HomeCheckedInOn, Is.Null);
            Assert.That(gate.Releases, Is.EqualTo(1));
        }

        [TestCase(WriteKind.Availability)]
        [TestCase(WriteKind.SetScheduled)]
        [TestCase(WriteKind.ClearSchedule)]
        [TestCase(WriteKind.CheckIn)]
        public async Task UnchangedPairing_WritesNormally_AndReleasesLock(WriteKind kind)
        {
            var (harness, tid, fixture, _) = await SetUpBracket();
            if (kind == WriteKind.Availability)
            {
                using var context = harness.ReadContext();
                var row = context.Set<MatchEntity>().Single(m => m.Id == fixture.Id);
                row.Status = MatchStatus.Pending;
                row.ScheduledStartTime = null;
                row.HomeSlotsJson = null;
                context.SaveChanges();
            }

            var gate = new LockGate();
            var pending = Write(ServiceFor(harness, fixture, kind, gate), fixture.Id!.Value, kind);
            await AwaitLock(pending, gate);
            gate.Resume.TrySetResult();
            await pending.WaitAsync(TimeSpan.FromSeconds(10));

            var saved = harness.Match(fixture.Id!.Value);
            Assert.Multiple(() =>
            {
                Assert.That(saved.HomeParticipantId, Is.EqualTo(fixture.HomeParticipantId));
                Assert.That(saved.AwayParticipantId, Is.EqualTo(fixture.AwayParticipantId));
                Assert.That(gate.WritesInsideLock, Is.GreaterThan(0));
                Assert.That(gate.WritesOutsideLock, Is.Zero);
                Assert.That(gate.Releases, Is.EqualTo(1));
                switch (kind)
                {
                    case WriteKind.Availability:
                        Assert.That(saved.HomeSlots, Has.Count.EqualTo(1));
                        Assert.That(saved.AwaySlots, Is.EqualTo(fixture.AwaySlots));
                        Assert.That(saved.Status, Is.EqualTo(MatchStatus.Scheduled), "overlap still schedules the match");
                        break;
                    case WriteKind.SetScheduled:
                        Assert.That(saved.Status, Is.EqualTo(MatchStatus.Scheduled));
                        Assert.That(saved.CheckInResolvedOn, Is.Not.Null);
                        break;
                    case WriteKind.ClearSchedule:
                        Assert.That(saved.Status, Is.EqualTo(MatchStatus.Pending));
                        Assert.That(saved.ScheduledStartTime, Is.Null);
                        Assert.That(saved.HomeSlotsJson, Is.Null);
                        Assert.That(saved.AwaySlotsJson, Is.Null);
                        break;
                    case WriteKind.CheckIn:
                        Assert.That(saved.HomeCheckedInOn, Is.Not.Null);
                        Assert.That(saved.AwayCheckedInOn, Is.Null);
                        break;
                }
            });
        }

        [TestCase(WriteKind.Availability)]
        [TestCase(WriteKind.SetScheduled)]
        [TestCase(WriteKind.ClearSchedule)]
        [TestCase(WriteKind.CheckIn)]
        public async Task ResultCommittedWhileWaiting_IsNotReopenedOrOverwritten(WriteKind kind)
        {
            var (harness, tid, fixture, _) = await SetUpBracket();
            var gate = new LockGate();
            var pending = Write(ServiceFor(harness, fixture, kind, gate), fixture.Id!.Value, kind);
            string afterResult;
            try
            {
                await AwaitLock(pending, gate);
                await harness.NewService().UpdateMatchResult(new DataModels.Models.MatchResultDto
                {
                    MatchId = fixture.Id!.Value, TournamentId = tid, HomeScore = 2, AwayScore = 1,
                });
                afterResult = Snapshot(harness, tid);
            }
            finally
            {
                gate.Resume.TrySetResult();
            }

            Assert.ThrowsAsync<BusinessRuleException>(async () => await pending.WaitAsync(TimeSpan.FromSeconds(10)));
            Assert.That(Snapshot(harness, tid), Is.EqualTo(afterResult));
            Assert.That(gate.Releases, Is.EqualTo(1));
        }

        [TestCase(WriteKind.SetScheduled)]
        [TestCase(WriteKind.ClearSchedule)]
        public async Task ScheduleWrite_DoesNotOverwriteUnrelatedColumns(WriteKind kind)
        {
            var (harness, _, fixture, _) = await SetUpBracket();
            var gate = new LockGate();
            // Another writer that does not use this lock can still change unrelated fields
            // between the protected read and write. Only schedule columns may be updated.
            gate.BeforeFirstWrite = () =>
            {
                using var context = harness.ReadContext();
                var row = context.Set<MatchEntity>().Single(m => m.Id == fixture.Id);
                row.AdminHelpRequested = true;
                row.BestOf = 5;
                context.SaveChanges();
            };
            var pending = Write(ServiceFor(harness, fixture, kind, gate), fixture.Id!.Value, kind);
            await AwaitLock(pending, gate);
            gate.Resume.TrySetResult();
            await pending.WaitAsync(TimeSpan.FromSeconds(10));

            var saved = harness.Match(fixture.Id!.Value);
            Assert.That(saved.AdminHelpRequested, Is.True);
            Assert.That(saved.BestOf, Is.EqualTo(5));
        }

        private static readonly DateTime KickOff = DateTime.UtcNow.AddMinutes(2);

        private static async Task<(BracketTestHarness Harness, Guid Tid, MatchEntity Fixture, MatchEntity Other)> SetUpBracket(bool team = false)
        {
            var harness = new BracketTestHarness(useSqlite: true);
            var tid = team
                ? await harness.SeedTeamTournamentAsync(TournamentFormat.SingleElimination, 4, teamSize: 1)
                : await harness.SeedSoloTournamentAsync(TournamentFormat.SingleElimination, 4);
            if (team)
            {
                foreach (var participant in harness.Participants(tid))
                    await harness.SeedTeamRosterAsync(participant.TeamId!.Value, tid, Guid.NewGuid());
                await harness.NewService().GenerateTeamSingleEliminationBracket(tid);
            }
            else
            {
                await harness.NewService().GenerateSingleEliminationBracket(tid);
            }
            // The swap endpoint edits order 2. Use the real generated knockout topology without
            // playing a whole group stage for every interleaving test.
            using (var context = harness.ReadContext())
            {
                context.Set<TournamentStageEntity>().Single(s => s.TournamentId == tid).Order = 2;
                context.Set<TournamentEntity>().Single(t => t.Id == tid).RequireMatchCheckIn = true;
                context.SaveChanges();
            }
            var firstRound = harness.Matches(tid).Where(m => m.RoundNumber == 1).ToList();
            harness.MarkScheduled(firstRound[0].Id!.Value, KickOff);
            return (harness, tid, harness.Match(firstRound[0].Id!.Value), firstRound[1]);
        }

        private static MatchService ServiceFor(BracketTestHarness harness, MatchEntity fixture, WriteKind kind, LockGate gate)
            => kind == WriteKind.ClearSchedule
                ? harness.NewMatchServiceAsUser(BracketTestHarness.OwnerUserId, "Admin", gate)
                : harness.NewMatchServiceAsUser(
                    fixture.HomeUserId ?? harness.ParticipantUserId(fixture.HomeParticipantId!.Value), interceptor: gate);

        private static Task Write(MatchService service, Guid matchId, WriteKind kind) => kind switch
        {
            WriteKind.Availability => service.SetAvailability(matchId, new() { KickOff }),
            WriteKind.SetScheduled => service.SetScheduled(matchId),
            WriteKind.ClearSchedule => service.ClearSchedule(matchId),
            WriteKind.CheckIn => service.CheckIn(matchId),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };

        private static async Task AwaitLock(Task pending, LockGate gate)
        {
            await Task.WhenAny(pending, gate.Entered.Task).WaitAsync(TimeSpan.FromSeconds(10));
            Assert.That(gate.Entered.Task.IsCompletedSuccessfully, Is.True,
                "the request must reach the tournament lock before any scheduling write");
        }

        private static string Snapshot(BracketTestHarness harness, Guid tid)
            => JsonSerializer.Serialize(harness.Matches(tid).OrderBy(m => m.Id).Select(m => new
            {
                m.Id, m.HomeParticipantId, m.AwayParticipantId, m.WinnerParticipantId, m.Status,
                m.HomeUserScore, m.AwayUserScore, m.GamesJson, m.ScheduledStartTime,
                m.HomeSlotsJson, m.AwaySlotsJson, m.HomeSlotsSetOn, m.AwaySlotsSetOn,
                m.HomeCheckedInOn, m.AwayCheckedInOn, m.CheckInResolvedOn,
            }));

        private sealed class LockGate : DbCommandInterceptor
        {
            public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public TaskCompletionSource Resume { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public Action? BeforeFirstWrite { get; set; }
            public int Releases { get; private set; }
            public int WritesInsideLock { get; private set; }
            public int WritesOutsideLock { get; private set; }
            private bool held;

            public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
                DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
                CancellationToken cancellationToken = default)
            {
                if (command.CommandText.Contains("pg_advisory_lock("))
                {
                    Entered.TrySetResult();
                    await Resume.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
                    held = true;
                }
                else if (command.CommandText.Contains("pg_advisory_unlock("))
                {
                    held = false;
                    Releases++;
                }
                else if (command.CommandText.TrimStart().StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase))
                {
                    if (held) WritesInsideLock++; else WritesOutsideLock++;
                    var callback = BeforeFirstWrite;
                    BeforeFirstWrite = null;
                    callback?.Invoke();
                }
                return result;
            }
        }
    }
}
