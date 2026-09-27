using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GameHubz.Data.Context;
using GameHubz.Data.Repository;
using GameHubz.DataModels.Domain;
using GameHubz.DataModels.Enums;
using GameHubz.DataModels.Models;
using GameHubz.Logic.Test.Bracket;
using GameHubz.Logic.Utility;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using NUnit.Framework;

namespace GameHubz.Logic.Test.Repositories;

[TestFixture]
public sealed class RepositoryCollectionQueryTests
{
    private SqliteConnection? connection;
    private ApplicationContext context = null!;
    private IDbContextTransaction? transaction;
    private bool usePostgres;
    private CommandRecorder commands = null!;
    private TournamentEntity tournament = null!;
    private TournamentStageEntity stage = null!;
    private TeamMatchEntity teamMatch = null!;
    private MatchEntity soloMatch = null!;
    private MatchEntity subMatch = null!;
    private UserEntity homeUser = null!;

    [SetUp]
    public async Task SetUp()
    {
        commands = new CommandRecorder();
        // Optional disposable PostgreSQL database for testing the actual production provider.
        // No configured application connection strings are used and no database is dropped.
        // All PostgreSQL test data is rolled back, including when assertions fail.
        var postgres = Environment.GetEnvironmentVariable("GAMEHUBZ_QUERY_TEST_POSTGRES");
        usePostgres = !string.IsNullOrEmpty(postgres);
        if (usePostgres)
        {
            context = new ApplicationContext(new DbContextOptionsBuilder<ApplicationContext>()
                .UseNpgsql(postgres)
                .ConfigureWarnings(w => w.Throw(RelationalEventId.MultipleCollectionIncludeWarning))
                .AddInterceptors(commands).Options);
        }
        else
        {
            connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            context = new TestApplicationContext(new DbContextOptionsBuilder<TestApplicationContext>()
                .UseSqlite(connection)
                // An unconfigured query with multiple collection joins must fail this suite.
                .ConfigureWarnings(w => w.Throw(RelationalEventId.MultipleCollectionIncludeWarning))
                .AddInterceptors(commands).Options);
        }
        await context.Database.EnsureCreatedAsync();
        if (usePostgres)
            transaction = await context.Database.BeginTransactionAsync();
    }

    [TearDown]
    public async Task TearDown()
    {
        try
        {
            if (transaction != null)
            {
                await using var rollbackTransaction = transaction;
                await rollbackTransaction.RollbackAsync();
            }
        }
        finally
        {
            transaction = null;
            await context.DisposeAsync();
            if (connection != null) await connection.DisposeAsync();
            connection = null;
        }
    }

    [TestCase(0)]
    [TestCase(3)]
    public async Task SoloEvidence_IsReadOnce_AndBothApiFieldsPreserveVisibleEvidence(int count)
    {
        await Seed(count);
        var result = await Repo<MatchRepository>().GetWithEvidence(soloMatch.Id!.Value);

        Assert.That(result, Is.Not.Null);
        AssertEvidence(result!.Evidences, result.EvidenceItems, count);
        Assert.That(commands.Sql, Has.Count.EqualTo(1));
        Assert.That(Occurrences(commands.Sql.Single(), "FROM \"MatchEvidence\""), Is.EqualTo(1));
    }

    [TestCase(0)]
    [TestCase(3)]
    public async Task TeamDetails_PreserveRostersOrderedSubMatchesAndEvidence(int count)
    {
        await UseTeamDetailsProvider();
        await Seed(count);
        var result = await Repo<TeamMatchRepository>().GetDetailsProjection(teamMatch.Id!.Value);

        Assert.That(result, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(result!.HomeTeam!.Members, Has.Count.EqualTo(2));
            Assert.That(result.AwayTeam!.Members, Has.Count.EqualTo(3));
            Assert.That(result.HomeTeam.Members.Count(m => m.IsReserve), Is.EqualTo(1));
            Assert.That(result.SubMatches.Select(m => m.MatchOrder), Is.EqualTo(new int?[] { 1, 2 }));
        });
        AssertEvidence(result!.SubMatches[0].Evidences, result.SubMatches[0].EvidenceItems, count);
        AssertEvidence(result.SubMatches[1].Evidences, result.SubMatches[1].EvidenceItems, 0);
        if (usePostgres)
            Assert.That(commands.Sql.Sum(sql => Occurrences(sql, "JOIN \"MatchEvidence\"")
                + Occurrences(sql, "FROM \"MatchEvidence\"")), Is.EqualTo(1));
    }

    [Test]
    public async Task MissingMatches_ReturnNull()
    {
        await UseTeamDetailsProvider();
        Assert.That(await Repo<MatchRepository>().GetWithEvidence(Guid.NewGuid()), Is.Null);
        Assert.That(await Repo<TeamMatchRepository>().GetDetailsProjection(Guid.NewGuid()), Is.Null);
    }

    [Test]
    public async Task TeamDetails_WithoutAssignedTeamsOrSubMatches_KeepEmptyCollections()
    {
        if (!usePostgres)
            Assert.Ignore("Requires GAMEHUBZ_QUERY_TEST_POSTGRES: SQLite lacks APPLY and InMemory cannot shape missing optional teams in this projection.");
        await Seed(0);
        var unassigned = new TeamMatchEntity { Id = Guid.NewGuid(), TournamentId = tournament.Id!.Value };
        context.Add(unassigned);
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var result = await Repo<TeamMatchRepository>().GetDetailsProjection(unassigned.Id!.Value);
        Assert.That(result, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(result!.HomeTeam, Is.Null);
            Assert.That(result.AwayTeam, Is.Null);
            Assert.That(result.SubMatches, Is.Empty);
        });
    }

    [TestCase("team-match")]
    [TestCase("tournament-pending")]
    [TestCase("stage")]
    public async Task EntityQueries_LoadAllRequestedCollectionsWithoutCrossProducts(string query)
    {
        await Seed(0);
        switch (query)
        {
            case "team-match":
                var teamResult = await Repo<TeamMatchRepository>().GetByIdWithSubMatches(teamMatch.Id!.Value);
                Assert.That(teamResult!.SubMatches, Has.Count.EqualTo(2));
                Assert.That(teamResult.HomeTeamParticipant!.Team!.Members, Has.Count.EqualTo(2));
                Assert.That(teamResult.AwayTeamParticipant!.Team!.Members, Has.Count.EqualTo(3));
                Assert.That(teamResult.HomeTeamParticipant.Team.Members.All(m => m.User != null), Is.True);
                break;
            case "tournament-pending":
                var pending = await Repo<TournamentRepository>().GetWithPendingRegistration(tournament.Id!.Value);
                Assert.That(pending.TournamentParticipants, Has.Count.EqualTo(4));
                Assert.That(pending.TournamentRegistrations, Has.Count.EqualTo(2));
                Assert.That(pending.TournamentRegistrations!.All(r => r.Status == TournamentRegistrationStatus.Pending), Is.True);
                break;
            case "stage":
                var stageResult = await Repo<TournamentStageRepository>().GetWithGroupsAndMatches(stage.Id!.Value);
                Assert.That(stageResult!.TournamentGroups, Has.Count.EqualTo(2));
                Assert.That(stageResult.Matches, Has.Count.EqualTo(3));
                break;
            default: throw new ArgumentOutOfRangeException(nameof(query));
        }
        Assert.That(commands.Sql.Count, Is.GreaterThan(1), "Independent collections should use separate SQL commands.");
    }

    private T Repo<T>() => (T)Activator.CreateInstance(typeof(T), context, new DateTimeProvider(), null, null, null)!;

    private async Task UseTeamDetailsProvider()
    {
        if (usePostgres) return;
        // This existing projection needs LATERAL/APPLY, unsupported by SQLite. Check DTO
        // semantics in memory by default, and execute its SQL when PostgreSQL is supplied.
        await context.DisposeAsync();
        context = new ApplicationContext(new DbContextOptionsBuilder<ApplicationContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        await context.Database.EnsureCreatedAsync();
    }

    private static void AssertEvidence(List<string> urls, List<MatchEvidenceItemDto> items, int count)
    {
        Assert.That(items, Has.Count.EqualTo(count));
        Assert.That(urls, Is.EqualTo(items.Select(e => e.Url)));
        if (count > 0)
        {
            Assert.That(urls.Count(u => u == "same-url"), Is.EqualTo(2), "Distinct evidence rows may share a URL.");
            Assert.That(items.Count(e => e.MediaType == EvidenceMediaType.Video), Is.EqualTo(1));
        }
    }

    private async Task Seed(int evidenceCount)
    {
        var role = new UserRoleEntity { Id = Guid.NewGuid() };
        UserEntity User(string name) => new() { Id = Guid.NewGuid(), Username = name, UserRole = role };
        homeUser = User("home");
        var awayUser = User("away");
        var hub = new HubEntity { Id = Guid.NewGuid(), User = homeUser };
        tournament = new TournamentEntity { Id = Guid.NewGuid(), Hub = hub };
        stage = new TournamentStageEntity { Id = Guid.NewGuid(), Tournament = tournament, Type = StageType.SingleEliminationBracket };
        TournamentTeamEntity Team(string name, UserEntity captain, int size)
        {
            var team = new TournamentTeamEntity { Id = Guid.NewGuid(), TeamName = name, Tournament = tournament, CaptainUser = captain };
            for (int i = 0; i < size; i++)
                team.Members.Add(new TournamentTeamMemberEntity { Id = Guid.NewGuid(), User = i == 0 ? captain : User(name + i), IsReserve = i == size - 1 });
            team.Members.Add(new TournamentTeamMemberEntity { Id = Guid.NewGuid(), User = User(name + "-deleted"), IsDeleted = true });
            return team;
        }
        var homeTeam = Team("home", homeUser, 2);
        var awayTeam = Team("away", awayUser, 3);
        TournamentParticipantEntity Participant(UserEntity? user, TournamentTeamEntity? team) => new()
        { Id = Guid.NewGuid(), Tournament = tournament, User = user, Team = team };
        var home = Participant(null, homeTeam);
        var away = Participant(null, awayTeam);
        teamMatch = new TeamMatchEntity
        {
            Id = Guid.NewGuid(), Tournament = tournament, TournamentStage = stage,
            HomeTeamParticipant = home, AwayTeamParticipant = away
        };
        subMatch = new MatchEntity
        {
            Id = Guid.NewGuid(), Tournament = tournament, TournamentStage = stage, TeamMatch = teamMatch,
            HomeParticipant = home, AwayParticipant = away, HomeUser = homeUser, AwayUser = awayUser, MatchOrder = 1
        };
        var second = new MatchEntity
        {
            Id = Guid.NewGuid(), Tournament = tournament, TournamentStage = stage, TeamMatch = teamMatch,
            HomeUser = homeUser, AwayUser = awayUser, MatchOrder = 2
        };
        soloMatch = new MatchEntity
        {
            Id = Guid.NewGuid(), Tournament = tournament, TournamentStage = stage,
            HomeParticipant = Participant(homeUser, null), AwayParticipant = Participant(awayUser, null)
        };
        foreach (var match in new[] { soloMatch, subMatch })
        {
            for (int i = 0; i < evidenceCount; i++)
                match.MatchEvidences!.Add(new MatchEvidenceEntity
                {
                    Id = Guid.NewGuid(), Url = i < 2 ? "same-url" : "other-url",
                    MediaType = i == 1 ? EvidenceMediaType.Video : EvidenceMediaType.Image
                });
            match.MatchEvidences!.Add(new MatchEvidenceEntity { Id = Guid.NewGuid(), Url = "deleted", IsDeleted = true });
        }
        context.AddRange(soloMatch, subMatch, second);
        for (int i = 0; i < 3; i++)
            context.Add(new TournamentGroupEntity { Id = Guid.NewGuid(), TournamentStage = stage, Name = "group" + i, IsDeleted = i == 2 });
        for (int i = 0; i < 4; i++)
            context.Add(new TournamentRegistrationEntity
            {
                Id = Guid.NewGuid(), Tournament = tournament, IsDeleted = i == 3,
                Status = i == 2 ? TournamentRegistrationStatus.Approved : TournamentRegistrationStatus.Pending
            });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        commands.Sql.Clear();
    }

    private static int Occurrences(string sql, string fragment) => sql.Split(fragment, StringSplitOptions.None).Length - 1;

    private sealed class CommandRecorder : DbCommandInterceptor
    {
        public List<string> Sql { get; } = new();
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Sql.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }
}
