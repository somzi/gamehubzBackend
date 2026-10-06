using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using GameHubz.Api.BackgroundTasks;
using GameHubz.DataModels.Config;
using GameHubz.DataModels.Domain;
using GameHubz.DataModels.Enums;
using GameHubz.Logic.Interfaces;
using GameHubz.Logic.Test.Bracket;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;

namespace GameHubz.Logic.Test.Services
{
    [TestFixture]
    internal sealed class DeadlineNotificationRunnerTests
    {
        private SqliteConnection connection = null!;
        private TestApplicationContext context = null!;
        private BeforeUsersReadInterceptor interceptor = null!;
        private Mock<INotificationService> notifications = null!;
        private Mock<IDiscordDmService> discord = null!;
        private Mock<ILogger<DeadlineNotificationRunner>> logger = null!;
        private DeadlineNotificationRunner runner = null!;
        private readonly List<LocalizedPush> pushes = new();
        private Func<Task>? afterPush;
        private MatchEntity match = null!;
        private UserEntity home = null!;
        private UserEntity away = null!;

        [SetUp]
        public async Task SetUp()
        {
            connection = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
            await connection.OpenAsync();
            interceptor = new BeforeUsersReadInterceptor();
            context = new TestApplicationContext(new DbContextOptionsBuilder<TestApplicationContext>()
                .UseSqlite(connection).AddInterceptors(interceptor).Options);
            await context.Database.EnsureCreatedAsync();

            home = new UserEntity { Id = Guid.NewGuid(), Username = "home", DiscordUserId = "home-discord" };
            away = new UserEntity { Id = Guid.NewGuid(), Username = "away", DiscordUserId = "away-discord" };
            var tournament = new TournamentEntity
            {
                Id = Guid.NewGuid(), Name = "Reminder test", Status = TournamentStatus.InProgress,
                Format = TournamentFormat.GroupStageWithKnockout, RoundDurationMinutes = 60,
            };
            var stage = new TournamentStageEntity
            {
                Id = Guid.NewGuid(), Tournament = tournament, Order = 1, Type = StageType.GroupStage,
            };
            match = new MatchEntity
            {
                Id = Guid.NewGuid(), Tournament = tournament, TournamentStage = stage,
                HomeParticipant = new TournamentParticipantEntity { Id = Guid.NewGuid(), Tournament = tournament, User = home },
                AwayParticipant = new TournamentParticipantEntity { Id = Guid.NewGuid(), Tournament = tournament, User = away },
                Status = MatchStatus.Pending, RoundNumber = 1, Stage = MatchStage.GroupStage,
                RoundOpenAt = DateTime.UtcNow.AddMinutes(-50), RoundDeadline = DateTime.UtcNow.AddMinutes(10),
            };
            context.Add(match);
            await context.SaveChangesAsync();

            pushes.Clear();
            afterPush = null;
            notifications = new Mock<INotificationService>();
            notifications.Setup(n => n.SendLocalizedBatchAsync(It.IsAny<IReadOnlyCollection<LocalizedPush>>()))
                .Returns(async (IReadOnlyCollection<LocalizedPush> batch) =>
                {
                    pushes.AddRange(batch);
                    if (afterPush != null) await afterPush();
                });
            discord = new Mock<IDiscordDmService>();
            discord.Setup(d => d.SendDmAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid?>())).Returns(Task.CompletedTask);
            var localization = new Mock<ILocalizationService>();
            localization.Setup(l => l[It.IsAny<string>(), It.IsAny<string>()]).Returns("Reminder");
            logger = new Mock<ILogger<DeadlineNotificationRunner>>();
            runner = new DeadlineNotificationRunner(
                context, notifications.Object, discord.Object, localization.Object,
                tournamentNotifier: null!, bracketService: null!, cacheService: new FakeCacheService(),
                Options.Create(new ShareLinksConfig()), new ConfigurationBuilder().Build(), logger.Object);
        }

        [TearDown]
        public void TearDown()
        {
            context.Dispose();
            connection.Dispose();
        }

        [TestCase(MatchStatus.Pending)]
        [TestCase(MatchStatus.Scheduled)]
        [TestCase(MatchStatus.Live)]
        public async Task UnplayedMatch_RemindsBothPlayersOnlyOnce(MatchStatus status)
        {
            match.Status = status;
            await context.SaveChangesAsync();

            await RunAsync();
            await RunAsync();

            Assert.That(pushes.Count, Is.EqualTo(1));
            Assert.That(pushes[0].Body.ResourceKey, Is.EqualTo("Push.RoundDeadlineFinal.Body"));
            Assert.That(pushes[0].Recipients.Select(r => r.UserId), Is.EquivalentTo(new[] { home.Id, away.Id }));
            discord.Verify(d => d.SendDmAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid?>()), Times.Exactly(2));
            Assert.That(await ReminderStageAsync(), Is.EqualTo(2));
        }

        [TestCase(MatchStatus.Completed)]
        [TestCase(MatchStatus.NoShow)]
        [TestCase(MatchStatus.TieBreakRequired)]
        public async Task FinishedOrReportedMatch_DoesNotSendPlayReminder(MatchStatus status)
        {
            match.Status = status;
            await context.SaveChangesAsync();

            await RunAsync();

            AssertNoReminders();
        }

        [Test]
        public async Task LongRound_EarlyReminderDoesNotConsumeTheFinalWave()
        {
            match.RoundOpenAt = DateTime.UtcNow.AddDays(-2);
            match.RoundDeadline = DateTime.UtcNow.AddHours(20);
            await context.SaveChangesAsync();

            await RunAsync();
            await RunAsync();
            Assert.That(pushes.Select(p => p.Body.ResourceKey), Is.EqualTo(new[] { "Push.RoundDeadline.Body" }));
            Assert.That(await ReminderStageAsync(), Is.EqualTo(1));

            // Move the fixture into the final window, retaining the persisted early-wave marker.
            await context.Set<MatchEntity>().Where(m => m.Id == match.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.RoundDeadline, DateTime.UtcNow.AddHours(2)));
            await RunAsync();
            await RunAsync();

            Assert.That(pushes.Select(p => p.Body.ResourceKey),
                Is.EqualTo(new[] { "Push.RoundDeadline.Body", "Push.RoundDeadlineFinal.Body" }));
            Assert.That(await ReminderStageAsync(), Is.EqualTo(2));
        }

        [Test]
        public async Task ResultAwaitingConfirmation_DoesNotSendPlayReminder()
        {
            match.ProposedByUserId = home.Id;
            match.ProposedHomeScore = 2;
            match.ProposedAwayScore = 1;
            await context.SaveChangesAsync();

            await RunAsync();

            AssertNoReminders();
        }

        [TestCase(true)]
        [TestCase(false)]
        public async Task WaitingForOpponent_DoesNotRemindThePlayerWhoAlreadyAdvanced(bool missingAway)
        {
            if (missingAway)
            {
                match.AwayParticipant = null;
                match.AwayParticipantId = null;
            }
            else
            {
                match.HomeParticipant = null;
                match.HomeParticipantId = null;
            }
            await context.SaveChangesAsync();

            await RunAsync();

            AssertNoReminders();
            Assert.That(await ReminderStageAsync(), Is.Zero, "keep the reminder available when the opponent arrives");
        }

        [Test]
        public async Task TeamSubMatch_UsesTheNominatedPlayers()
        {
            var tie = new TeamMatchEntity { Id = Guid.NewGuid(), TournamentId = match.TournamentId, Status = TeamMatchStatus.Pending };
            context.Add(tie);
            match.TeamMatch = tie;
            match.HomeParticipant!.User = null;
            match.HomeParticipant.UserId = null;
            match.AwayParticipant!.User = null;
            match.AwayParticipant.UserId = null;
            match.HomeUserId = home.Id;
            match.AwayUserId = away.Id;
            await context.SaveChangesAsync();

            await RunAsync();

            Assert.That(pushes.Count, Is.EqualTo(1));
            Assert.That(pushes[0].Recipients.Select(r => r.UserId), Is.EquivalentTo(new[] { home.Id, away.Id }));
            discord.Verify(d => d.SendDmAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid?>()), Times.Exactly(2));
        }

        [TestCase("completed")]
        [TestCase("proposed")]
        [TestCase("rescheduled")]
        public async Task MatchChangesWhileRecipientsLoad_SkipsStaleReminder(string change)
        {
            interceptor.BeforeRead = () => ChangeMatchAsync(change);

            await RunAsync();

            AssertNoReminders();
            Assert.That(await ReminderStageAsync(), Is.Zero);
        }

        [TestCase("completed")]
        [TestCase("proposed")]
        [TestCase("rescheduled")]
        public async Task MatchChangesAfterPush_SkipsStaleDiscordReminder(string change)
        {
            afterPush = () => ChangeMatchAsync(change);

            await RunAsync();

            Assert.That(pushes.Count, Is.EqualTo(1), "the push was sent while the match was still unplayed");
            discord.Verify(d => d.SendDmAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid?>()), Times.Never);
            if (change == "rescheduled")
                Assert.That(await ReminderStageAsync(), Is.Zero, "the old push must not consume the new deadline's reminder");
        }

        [Test]
        public async Task ParticipantSwappedWhilePushIsSent_LeavesTheReminderForTheNewPair()
        {
            // The organizer swaps a player in while the old pair's reminder is going out; the swap re-arms
            // the reminder, and the old send's marker must not land on the new pairing.
            var newcomer = new UserEntity { Id = Guid.NewGuid(), Username = "newcomer" };
            afterPush = () => SwapHomeAsync(newcomer);

            await RunAsync();
            Assert.That(await ReminderStageAsync(), Is.Zero, "the old pair's send must not consume the new pair's reminder");

            afterPush = null;
            await RunAsync();

            Assert.That(pushes.Count, Is.EqualTo(2));
            Assert.That(pushes[1].Recipients.Select(r => r.UserId), Is.EquivalentTo(new[] { newcomer.Id, away.Id }));
            Assert.That(await ReminderStageAsync(), Is.EqualTo(2));
        }

        [Test]
        public async Task MatchCompletedDuringDiscordFanOut_SkipsRemainingDm()
        {
            discord.Setup(d => d.SendDmAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid?>()))
                .Returns(() => ChangeMatchAsync("completed"));

            await RunAsync();

            discord.Verify(d => d.SendDmAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid?>()), Times.Once);
        }

        private async Task RunAsync()
        {
            await runner.RunAsync(CancellationToken.None);
            Assert.That(logger.Invocations.Where(i => i.Method.Name == "Log" && (LogLevel)i.Arguments[0] >= LogLevel.Warning),
                Is.Empty, "a swallowed sweep exception must not make a no-notification test pass");
        }

        private void AssertNoReminders()
        {
            Assert.That(pushes, Is.Empty);
            discord.Verify(d => d.SendDmAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Guid?>()), Times.Never);
        }

        private Task<int> ReminderStageAsync()
            => context.Set<MatchEntity>().Where(m => m.Id == match.Id).Select(m => m.RoundReminderStage).SingleAsync();

        // A separate context simulates a result request while the sweep holds an older snapshot.
        private async Task ChangeMatchAsync(string change)
        {
            await using var writer = new TestApplicationContext(new DbContextOptionsBuilder<TestApplicationContext>()
                .UseSqlite(connection).Options);
            var query = writer.Set<MatchEntity>().Where(m => m.Id == match.Id);
            if (change == "completed")
                await query.ExecuteUpdateAsync(s => s.SetProperty(m => m.Status, MatchStatus.Completed));
            else if (change == "proposed")
                await query.ExecuteUpdateAsync(s => s.SetProperty(m => m.ProposedByUserId, home.Id)
                    .SetProperty(m => m.ProposedHomeScore, 2).SetProperty(m => m.ProposedAwayScore, 1));
            else
                await query.ExecuteUpdateAsync(s => s.SetProperty(m => m.RoundDeadline, DateTime.UtcNow.AddDays(2))
                    .SetProperty(m => m.RoundReminderStage, 0));
        }

        // What BracketService does when a participant is replaced: a new home side, the reminder re-armed.
        private async Task SwapHomeAsync(UserEntity newcomer)
        {
            await using var writer = new TestApplicationContext(new DbContextOptionsBuilder<TestApplicationContext>()
                .UseSqlite(connection).Options);
            var participant = new TournamentParticipantEntity { Id = Guid.NewGuid(), TournamentId = match.TournamentId, User = newcomer };
            writer.Add(participant);
            await writer.SaveChangesAsync();
            await writer.Set<MatchEntity>().Where(m => m.Id == match.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.HomeParticipantId, participant.Id)
                    .SetProperty(m => m.RoundReminderStage, 0));
        }

        private sealed class BeforeUsersReadInterceptor : DbCommandInterceptor
        {
            public Func<Task>? BeforeRead { get; set; }

            public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
                DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
                CancellationToken cancellationToken = default)
            {
                if (BeforeRead != null && command.CommandText.Contains("FROM \"User\" AS"))
                {
                    var callback = BeforeRead;
                    BeforeRead = null;
                    await callback();
                }
                return result;
            }
        }
    }
}
