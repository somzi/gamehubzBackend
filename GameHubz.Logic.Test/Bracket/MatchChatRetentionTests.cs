using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

using NUnit.Framework;

using GameHubz.Api.BackgroundTasks;
using GameHubz.DataModels.Domain;
using GameHubz.DataModels.Enums;

namespace GameHubz.Logic.Test.Bracket
{
    // The chat retention sweep deletes rows PERMANENTLY — there is no undo and no export. So what is
    // pinned here is mostly the negative space: which threads it must not touch. A regression that
    // widens the window is not a failed assertion in production, it is somebody's match history gone.
    //
    // Runs on SQLite rather than the in-memory provider the other suites use: the sweep is built on
    // ExecuteDelete, which the in-memory provider cannot translate.
    [TestFixture]
    internal sealed class MatchChatRetentionTests
    {
        private const int GraceDays = 3;
        private const int AbandonedDays = 180;

        private SqliteConnection connection = null!;
        private DbContextOptions<TestApplicationContext> options = null!;

        [SetUp]
        public void SetUp()
        {
            // Foreign Keys=False for the same reason the bracket harness does it: this suite is about
            // the retention predicate, not referential integrity, so matches need no participant graph.
            connection = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
            connection.Open();

            options = new DbContextOptionsBuilder<TestApplicationContext>()
                .UseSqlite(connection)
                .Options;

            using var init = new TestApplicationContext(options);
            init.Database.EnsureCreated();
        }

        [TearDown]
        public void TearDown() => connection.Dispose();

        [Test]
        public async Task SettledTournament_LosesItsChatAndItsReadCursors()
        {
            Guid matchId = Seed(endedOn: DateTime.UtcNow.AddDays(-(GraceDays + 1)));

            await SweepAsync();

            Assert.That(RemainingMessages(matchId), Is.EqualTo(0));
            Assert.That(RemainingCursors(matchId), Is.EqualTo(0), "a read cursor pointing at nothing is dead weight");
        }

        [Test]
        public async Task TournamentInsideTheGraceWindow_IsLeftAlone()
        {
            // The window exists so a disputed result can still be argued from the thread. One day
            // after the final whistle is exactly when someone opens a ticket about it.
            Guid matchId = Seed(endedOn: DateTime.UtcNow.AddDays(-1));

            await SweepAsync();

            Assert.That(RemainingMessages(matchId), Is.EqualTo(2));
            Assert.That(RemainingCursors(matchId), Is.EqualTo(1));
        }

        [Test]
        public async Task LiveTournament_IsLeftAlone()
        {
            Guid matchId = Seed(endedOn: null, tournamentCreatedOn: DateTime.UtcNow.AddDays(-10));

            await SweepAsync();

            Assert.That(RemainingMessages(matchId), Is.EqualTo(2), "nothing has ended — this thread is in use");
        }

        [Test]
        public async Task AbandonedTournament_IsCollectedByTheAgeBackstop()
        {
            // Never reached a terminal state, so EndedOn is null forever and the normal rule can
            // never fire. Without the backstop this thread outlives the company.
            Guid matchId = Seed(endedOn: null, tournamentCreatedOn: DateTime.UtcNow.AddDays(-(AbandonedDays + 1)));

            await SweepAsync();

            Assert.That(RemainingMessages(matchId), Is.EqualTo(0));
            Assert.That(RemainingCursors(matchId), Is.EqualTo(0));
        }

        [Test]
        public async Task LongRunningTournament_KeepsItsOldMessages()
        {
            // The backstop is keyed off the tournament's age, not the message's. A league that has
            // been running for two months must not lose its opening rounds while it is still being
            // played — that is the failure a per-message cut-off would have produced.
            Guid matchId = Seed(
                endedOn: null,
                tournamentCreatedOn: DateTime.UtcNow.AddDays(-60),
                messageCreatedOn: DateTime.UtcNow.AddDays(-59));

            await SweepAsync();

            Assert.That(RemainingMessages(matchId), Is.EqualTo(2));
        }

        [Test]
        public async Task SoftDeletedTournament_IsStillCollected()
        {
            // Every table involved carries an IsDeleted query filter. Left to it, the chat of a
            // deleted tournament would be invisible to the sweep and therefore kept forever — the
            // exact opposite of what deleting a tournament is supposed to mean.
            Guid matchId = Seed(
                endedOn: DateTime.UtcNow.AddDays(-(GraceDays + 1)),
                tournamentIsDeleted: true);

            await SweepAsync();

            Assert.That(RemainingMessages(matchId), Is.EqualTo(0));
        }

        [Test]
        public async Task OtherTournamentsChat_IsUntouchedBySweepOfAnother()
        {
            Guid expired = Seed(endedOn: DateTime.UtcNow.AddDays(-(GraceDays + 1)));
            Guid live = Seed(endedOn: null, tournamentCreatedOn: DateTime.UtcNow.AddDays(-2));

            await SweepAsync();

            Assert.That(RemainingMessages(expired), Is.EqualTo(0));
            Assert.That(RemainingMessages(live), Is.EqualTo(2), "the sweep must be surgical, not a table truncate");
        }

        // ─────────────────────────── helpers ───────────────────────────

        /// <summary>Seeds one tournament + one match + two messages + one read cursor. Returns the match id.</summary>
        private Guid Seed(
            DateTime? endedOn,
            DateTime? tournamentCreatedOn = null,
            DateTime? messageCreatedOn = null,
            bool tournamentIsDeleted = false)
        {
            using var context = new TestApplicationContext(options);

            var tournamentId = Guid.NewGuid();
            var matchId = Guid.NewGuid();
            var userId = Guid.NewGuid();
            DateTime created = tournamentCreatedOn ?? DateTime.UtcNow.AddDays(-30);
            DateTime sent = messageCreatedOn ?? created.AddDays(1);

            context.Set<TournamentEntity>().Add(new TournamentEntity
            {
                Id = tournamentId,
                Name = "Retention fixture",
                Status = endedOn == null ? TournamentStatus.InProgress : TournamentStatus.Completed,
                CreatedOn = created,
                EndedOn = endedOn,
                IsDeleted = tournamentIsDeleted,
            });

            context.Set<MatchEntity>().Add(new MatchEntity
            {
                Id = matchId,
                TournamentId = tournamentId,
                CreatedOn = created,
            });

            context.Set<MatchChatEntity>().AddRange(
                new MatchChatEntity { Id = Guid.NewGuid(), MatchId = matchId, UserId = userId, Content = "20:00 ok?", CreatedOn = sent },
                new MatchChatEntity { Id = Guid.NewGuid(), MatchId = matchId, UserId = userId, Content = "gg", CreatedOn = sent });

            context.Set<MatchChatReadEntity>().Add(new MatchChatReadEntity
            {
                Id = Guid.NewGuid(),
                MatchId = matchId,
                UserId = userId,
                LastReadAt = sent,
                CreatedOn = sent,
            });

            context.SaveChanges();
            return matchId;
        }

        private Task SweepAsync()
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["MatchChat:RetentionDaysAfterTournamentEnd"] = GraceDays.ToString(),
                    ["MatchChat:AbandonedTournamentMaxAgeDays"] = AbandonedDays.ToString(),
                })
                .Build();

            var runner = new MatchChatRetentionRunner(
                new TestApplicationContext(options),
                configuration,
                NullLogger<MatchChatRetentionRunner>.Instance);

            return runner.RunRetentionSweepAsync();
        }

        private int RemainingMessages(Guid matchId)
        {
            using var context = new TestApplicationContext(options);
            return context.Set<MatchChatEntity>().IgnoreQueryFilters().Count(x => x.MatchId == matchId);
        }

        private int RemainingCursors(Guid matchId)
        {
            using var context = new TestApplicationContext(options);
            return context.Set<MatchChatReadEntity>().IgnoreQueryFilters().Count(x => x.MatchId == matchId);
        }
    }
}
