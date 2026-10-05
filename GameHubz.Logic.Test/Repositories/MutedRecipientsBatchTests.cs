using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GameHubz.Data.Repository;
using GameHubz.DataModels.Domain;
using GameHubz.DataModels.Enums;
using GameHubz.DataModels.Models;
using GameHubz.Logic.Test.Bracket;
using GameHubz.Logic.Utility;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using NUnit.Framework;

namespace GameHubz.Logic.Test.Repositories;

/// <summary>
/// The mute read for a whole notification batch: the same answer as asking notification by notification,
/// from a bounded number of queries however large the batch is.
/// </summary>
[TestFixture]
public sealed class MutedRecipientsBatchTests
{
    private SqliteConnection connection = null!;
    private TestApplicationContext context = null!;
    private CommandCounter commands = null!;
    private UserRepository repository = null!;

    private readonly Guid hub1 = Guid.NewGuid();
    private readonly Guid hub2 = Guid.NewGuid();
    private readonly Guid tournament1 = Guid.NewGuid();
    private readonly Guid tournament2 = Guid.NewGuid();
    private readonly Guid match1 = Guid.NewGuid();
    private readonly Guid teamMatch2 = Guid.NewGuid();
    private readonly Guid hubMuter = Guid.NewGuid();
    private readonly Guid tournamentMuter = Guid.NewGuid();
    private readonly Guid listener = Guid.NewGuid();

    [SetUp]
    public async Task SetUp()
    {
        commands = new CommandCounter();
        connection = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
        await connection.OpenAsync();
        context = new TestApplicationContext(new DbContextOptionsBuilder<TestApplicationContext>()
            .UseSqlite(connection).AddInterceptors(commands).Options);
        await context.Database.EnsureCreatedAsync();

        context.AddRange(
            new HubEntity { Id = hub1, Name = "Hub one" },
            new HubEntity { Id = hub2, Name = "Hub two" },
            new TournamentEntity { Id = tournament1, Name = "Cup one", HubId = hub1 },
            new TournamentEntity { Id = tournament2, Name = "Cup two", HubId = hub2 },
            new MatchEntity { Id = match1, TournamentId = tournament1, Status = MatchStatus.Pending },
            new TeamMatchEntity { Id = teamMatch2, TournamentId = tournament2, Status = TeamMatchStatus.Pending },
            new UserEntity { Id = hubMuter, Username = "mutes-hub-one", MutedHubIdsJson = JsonSerializer.Serialize(new List<Guid> { hub1 }) },
            new UserEntity { Id = tournamentMuter, Username = "mutes-cup-two", PushToken = "token-cup-two", MutedTournamentIdsJson = JsonSerializer.Serialize(new List<Guid> { tournament2 }) },
            new UserEntity { Id = listener, Username = "mutes-nothing" });
        await context.SaveChangesAsync();

        repository = new UserRepository(context, new DateTimeProvider(), null!, null!, null!);
    }

    [TearDown]
    public async Task TearDown()
    {
        await context.DisposeAsync();
        await connection.DisposeAsync();
    }

    [Test]
    public async Task Batch_AnswersEachNotificationLikeTheSingleRead_InBoundedQueries()
    {
        var items = new List<MutedRecipientsQuery>
        {
            // A match of cup one, hub one: the hub's muter is out.
            Query(new NotificationScope(MatchId: match1), hubMuter, tournamentMuter, listener),
            // A team match of cup two, reached by push token only.
            new(new NotificationScope(TeamMatchId: teamMatch2), new[] { hubMuter }, new[] { "token-cup-two" }),
            // Hub two itself: muting cup two is not muting its hub.
            Query(new NotificationScope(HubId: hub2), tournamentMuter, listener),
            // Cup two by id, its hub resolved from it.
            Query(new NotificationScope(TournamentId: tournament2), tournamentMuter),
            // Nothing to do with a hub or a tournament.
            Query(new NotificationScope(), hubMuter),
        };

        commands.Count = 0;
        var batch = await repository.GetMutedNotificationRecipientsBatch(items);
        int batchQueries = commands.Count;

        Assert.That(batch.Select(muted => muted.Select(m => m.UserId)), Is.EqualTo(new[]
        {
            new[] { hubMuter },
            new[] { tournamentMuter },
            Array.Empty<Guid>(),
            new[] { tournamentMuter },
            Array.Empty<Guid>(),
        }));
        Assert.That(batchQueries, Is.LessThanOrEqualTo(4), "preferences, matches, team matches, tournaments — once each");

        // The same answers the per-notification read gives.
        for (int i = 0; i < items.Count; i++)
        {
            var single = await repository.GetMutedNotificationRecipients(items[i].Scope,
                items[i].UserIds.ToList(), items[i].PushTokens.ToList(), new List<string>());
            Assert.That(batch[i].Select(m => m.UserId), Is.EquivalentTo(single.Select(m => m.UserId)), $"notification {i}");
        }
    }

    [Test]
    public async Task Batch_WhereNobodyMutedAnything_IsOneQuery()
    {
        var items = Enumerable.Range(0, 50)
            .Select(_ => Query(new NotificationScope(MatchId: match1), listener))
            .ToList();

        commands.Count = 0;
        var batch = await repository.GetMutedNotificationRecipientsBatch(items);

        Assert.That(batch, Has.Count.EqualTo(50));
        Assert.That(batch.All(muted => muted.Count == 0), Is.True);
        Assert.That(commands.Count, Is.EqualTo(1), "a sweep where nobody muted anything reads the preferences and stops");
    }

    private static MutedRecipientsQuery Query(NotificationScope scope, params Guid[] userIds)
        => new(scope, userIds, Array.Empty<string>());

    private sealed class CommandCounter : DbCommandInterceptor
    {
        public int Count { get; set; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Count++;
            return ValueTask.FromResult(result);
        }
    }
}
