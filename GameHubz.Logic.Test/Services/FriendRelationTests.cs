using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using GameHubz.Common.Interfaces;
using GameHubz.Common.Models;
using GameHubz.DataModels.Domain;
using GameHubz.DataModels.Enums;
using GameHubz.DataModels.Models;
using GameHubz.Logic.Exceptions;
using GameHubz.Logic.Interfaces;
using GameHubz.Logic.Services;
using GameHubz.Logic.SignalR;
using GameHubz.Logic.Test.Bracket;
using GameHubz.Logic.Test.Factories;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Npgsql;
using NUnit.Framework;

namespace GameHubz.Logic.Test.Services
{
    [TestFixture]
    internal sealed class FriendRelationTests
    {
        private SqliteConnection connection = null!;
        private DbContextOptions<TestApplicationContext> options = null!;
        private readonly List<TestApplicationContext> contexts = new();
        private FakeCacheService cache = null!;
        private Guid a, b;

        [SetUp]
        public async Task SetUp()
        {
            connection = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
            await connection.OpenAsync();
            // Run the migration's exact SQL; only these PostgreSQL scalar functions need adapters.
            connection.CreateFunction<string, string, string>("LEAST", (x, y) => string.CompareOrdinal(x, y) < 0 ? x : y, true);
            connection.CreateFunction<string, string, string>("GREATEST", (x, y) => string.CompareOrdinal(x, y) > 0 ? x : y, true);
            options = new DbContextOptionsBuilder<TestApplicationContext>().UseSqlite(connection).Options;
            cache = new FakeCacheService();
            a = Guid.NewGuid(); b = Guid.NewGuid();
            var context = NewContext();
            await context.Database.EnsureCreatedAsync();
            var role = new UserRoleEntity { Id = Guid.NewGuid(), DisplayName = "User", SystemName = "User" };
            context.AddRange(role,
                new UserEntity { Id = a, Username = "A", UserRoleId = role.Id!.Value },
                new UserEntity { Id = b, Username = "B", UserRoleId = role.Id.Value });
            await context.SaveChangesAsync();
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var context in contexts) context.Dispose();
            contexts.Clear();
            connection.Dispose();
        }

        [Test]
        public async Task ExistingRequestHasOppositeDirectionsAndTheSameIdOnBothProfiles()
        {
            var id = await SeedRequest(a, b);
            var outgoing = await Service(a).GetRelationStatus(b);
            var incoming = await Service(b).GetRelationStatus(a);
            Assert.That(outgoing.Status, Is.EqualTo(FriendRelationStatus.OutgoingRequest));
            Assert.That(incoming.Status, Is.EqualTo(FriendRelationStatus.IncomingRequest));
            Assert.That(outgoing.RequestId, Is.EqualTo(id));
            Assert.That(incoming.RequestId, Is.EqualTo(id));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task RepeatedOrOppositeSendDoesNotInsertOrImplicitlyAccept(bool reverse)
        {
            await ApplyMigration();
            var id = await SeedRequest(a, b);
            Assert.ThrowsAsync<BusinessRuleException>(() => Service(reverse ? b : a).SendRequest(reverse ? a : b));
            var context = NewContext();
            Assert.That(context.Set<FriendRequestEntity>().Count(), Is.EqualTo(1));
            Assert.That(context.Set<FriendRequestEntity>().Single().Status, Is.EqualTo(FriendRequestStatus.Pending));
            Assert.That(context.Set<FriendshipEntity>().Any(), Is.False);
            Assert.That((await Service(b).GetRelationStatus(a)).RequestId, Is.EqualTo(id));
        }

        [Test]
        public async Task AcceptCreatesOneFriendshipAndInvalidatesBothPreviouslyReadProfiles()
        {
            var id = await SeedRequest(a, b);
            await Service(a).GetRelationStatus(b);
            await Service(b).GetRelationStatus(a);
            await Service(b).AcceptRequest(id);
            Assert.That((await Service(a).GetRelationStatus(b)).Status, Is.EqualTo(FriendRelationStatus.Friends));
            Assert.That((await Service(b).GetRelationStatus(a)).Status, Is.EqualTo(FriendRelationStatus.Friends));
            var context = NewContext();
            Assert.That(context.Set<FriendshipEntity>().Count(), Is.EqualTo(1));
            Assert.That(context.Set<FriendRequestEntity>().Single().Status, Is.EqualTo(FriendRequestStatus.Accepted));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task RejectOrCancelClearsTheProfileActionWithoutCreatingFriendship(bool cancel)
        {
            var id = await SeedRequest(a, b);
            if (cancel) await Service(a).CancelRequest(id);
            else await Service(b).RejectRequest(id);
            Assert.That((await Service(a).GetRelationStatus(b)).Status, Is.EqualTo(FriendRelationStatus.None));
            Assert.That((await Service(b).GetRelationStatus(a)).Status, Is.EqualTo(FriendRelationStatus.None));
            Assert.That(NewContext().Set<FriendshipEntity>().Any(), Is.False);
        }

        [Test]
        public async Task SenderCannotAcceptTheirOwnRequest()
        {
            var id = await SeedRequest(a, b);
            Assert.ThrowsAsync<BusinessRuleException>(() => Service(a).AcceptRequest(id));
            Assert.That((await Service(b).GetRelationStatus(a)).Status, Is.EqualTo(FriendRelationStatus.IncomingRequest));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task DatabaseRejectsDuplicatePendingPairsInEitherDirection(bool reverse)
        {
            await ApplyMigration();
            await SeedRequest(a, b);
            Assert.ThrowsAsync<DbUpdateException>(() => SeedRequest(reverse ? b : a, reverse ? a : b));
            Assert.That(NewContext().Set<FriendRequestEntity>().Count(), Is.EqualTo(1));
        }

        [Test]
        public async Task MigrationPreservesOldestDirectionAndKeepsDuplicateHistoryAsCancelled()
        {
            var oldest = await SeedRequest(b, a, created: DateTime.UtcNow.AddDays(-2));
            await SeedRequest(a, b, created: DateTime.UtcNow.AddDays(-1));
            await SeedRequest(b, a);
            await ApplyMigration();
            var rows = NewContext().Set<FriendRequestEntity>().ToList();
            Assert.That(rows.Count, Is.EqualTo(3));
            Assert.That(rows.Single(r => r.Status == FriendRequestStatus.Pending).Id, Is.EqualTo(oldest));
            Assert.That(rows.Count(r => r.Status == FriendRequestStatus.Cancelled), Is.EqualTo(2));
        }

        [Test]
        public async Task AnsweredRequestsDoNotBlockAFutureRequest()
        {
            await ApplyMigration();
            await SeedRequest(a, b, FriendRequestStatus.Rejected);
            await SeedRequest(b, a, FriendRequestStatus.Cancelled);
            await SeedRequest(a, b);
            Assert.That(NewContext().Set<FriendRequestEntity>().Count(), Is.EqualTo(3));
        }

        [Test]
        public void OnlyThePendingPairConstraintBecomesAFriendlyConflict()
        {
            var method = typeof(FriendService).GetMethod("IsPendingPairConflict", BindingFlags.Static | BindingFlags.NonPublic)!;
            var pair = new PostgresException("duplicate", "ERROR", "ERROR", "23505", constraintName: "UX_FriendRequest_PendingPair");
            var unrelated = new PostgresException("duplicate", "ERROR", "ERROR", "23505", constraintName: "unrelated");
            Assert.That(method.Invoke(null, new object[] { new DbUpdateException("save", pair) }), Is.True);
            Assert.That(method.Invoke(null, new object[] { unrelated }), Is.False);
        }

        private TestApplicationContext NewContext()
        {
            var context = new TestApplicationContext(options);
            contexts.Add(context);
            return context;
        }

        private async Task<Guid> SeedRequest(Guid from, Guid to, FriendRequestStatus status = FriendRequestStatus.Pending, DateTime? created = null)
        {
            var context = NewContext();
            var row = new FriendRequestEntity { Id = Guid.NewGuid(), FromUserId = from, ToUserId = to,
                Status = status, CreatedOn = created ?? DateTime.UtcNow };
            context.Add(row);
            await context.SaveChangesAsync();
            return row.Id!.Value;
        }

        private Task ApplyMigration()
        {
            var source = File.ReadAllText(Path.Combine(TestContext.CurrentContext.TestDirectory, "Fixtures", "Migration_00103_Unique_Pending_Friend_Request.cs"));
            var sql = source.Split(new[] { new string('"', 3) }, StringSplitOptions.None)[1];
            return NewContext().Database.ExecuteSqlRawAsync(sql);
        }

        private FriendService Service(Guid caller)
        {
            var localization = new LocalizationServiceFactory().CreateService();
            var factory = new TestUnitOfWorkFactory(NewContext(), localization);
            var token = new TokenUserInfo { UserId = caller, Username = caller == a ? "A" : "B" };
            var reader = new Mock<IUserContextReader>();
            reader.Setup(r => r.GetTokenUserInfoFromContextThrowIfNull()).ReturnsAsync(token);
            reader.Setup(r => r.GetTokenUserInfoFromContext()).ReturnsAsync(token);
            var badges = new BadgeService(factory, reader.Object, localization, new Mock<IHubContext<UserHub>>().Object,
                new BadgeRefreshQueue(new Mock<IServiceScopeFactory>().Object, NullLogger<BadgeRefreshQueue>.Instance));
            return new FriendService(factory, localization, reader.Object, new Mock<INotificationService>().Object, cache, badges);
        }
    }
}
