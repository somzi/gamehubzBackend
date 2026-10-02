using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
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
using Microsoft.EntityFrameworkCore.Storage;
using NUnit.Framework;

namespace GameHubz.Logic.Test.Repositories;

[TestFixture]
public sealed class ProfileMatchQueryTests
{
    private SqliteConnection? connection;
    private ApplicationContext context = null!;
    private IDbContextTransaction? transaction;
    private readonly Dictionary<string, Guid> users = new();

    [SetUp]
    public async Task SetUp()
    {
        // Only an explicitly supplied disposable database is used for PostgreSQL tests.
        // Application connection strings are never read. Test rows are rolled back.
        var postgres = Environment.GetEnvironmentVariable("GAMEHUBZ_QUERY_TEST_POSTGRES");
        if (!string.IsNullOrEmpty(postgres))
        {
            context = new ApplicationContext(new DbContextOptionsBuilder<ApplicationContext>()
                .UseNpgsql(postgres).Options);
        }
        else
        {
            connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            context = new TestApplicationContext(new DbContextOptionsBuilder<TestApplicationContext>()
                .UseSqlite(connection).Options);
        }
        await context.Database.EnsureCreatedAsync();
        if (!string.IsNullOrEmpty(postgres))
            transaction = await context.Database.BeginTransactionAsync();
        await Seed();
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
            users.Clear();
        }
    }

    [TestCase("solo", 16)]
    [TestCase("opponent", 12)]
    [TestCase("team-only", 2)]
    [TestCase("deleted-participant", 0)]
    [TestCase("unrelated", 2)]
    [TestCase("missing", 0)]
    public async Task ProfileQueries_PreserveLegacyMembershipResultsOrderingAndPages(string user, int count)
    {
        var userId = users[user];
        // Keep the original navigation-based predicate as an independent SQL baseline.
        // Includes hydrate only reference navigations for expected DTOs; soft-delete filters
        // still come from the production EF model.
        var legacy = await context.Set<MatchEntity>().AsNoTracking()
            .Where(m => m.Status == MatchStatus.Completed &&
                ((m.TeamMatchId == null && m.HomeParticipantId != null && m.AwayParticipantId != null &&
                    (m.HomeParticipant!.UserId == userId || m.AwayParticipant!.UserId == userId))
                || (m.TeamMatchId != null && m.HomeUserId != null && m.AwayUserId != null &&
                    (m.HomeUserId == userId || m.AwayUserId == userId))))
            .Include(m => m.HomeParticipant).ThenInclude(p => p!.User)
            .Include(m => m.AwayParticipant).ThenInclude(p => p!.User)
            .Include(m => m.HomeUser)
            .Include(m => m.AwayUser)
            .Include(m => m.Tournament).ThenInclude(t => t!.Hub)
            .ToListAsync();
        Assert.That(legacy, Has.Count.EqualTo(count), "Fixture must cover the intended membership cases.");

        var repo = new MatchRepository(context, new DateTimeProvider(), null!, null!, null!);
        var expectedStats = new PlayerStatsDto
        {
            TotalMatches = legacy.Count,
            Wins = legacy.Count(m => Outcome(m, userId) == "W"),
            Losses = legacy.Count(m => Outcome(m, userId) == "L")
        };
        EqualJson(await repo.GetStatsByUserId(userId), expectedStats);

        var outcomes = legacy.OrderByDescending(m => m.ScheduledStartTime ?? m.ModifiedOn)
            .Select(m => Outcome(m, userId)).ToList();
        Assert.That(await repo.GetOutcomesByUserId(userId), Is.EqualTo(outcomes));
        Assert.That((await repo.GetPerformanceByUserIdV2(userId)).Select(p => p.Outcome),
            Is.EqualTo(outcomes.Take(10)));
        Assert.That((await repo.GetPerformanceByUserId(userId)).Select(p => p.IsWin),
            Is.EqualTo(legacy.OrderByDescending(m => m.ModifiedOn).Take(10)
                .Select(m => Outcome(m, userId) == "W")));

        var history = legacy.OrderBy(m => m.ScheduledStartTime == null)
            .ThenByDescending(m => m.ScheduledStartTime).Select(m => HistoryItem(m, userId)).ToList();
        // Check consecutive pages, the null scheduled date, and a page beyond the end.
        for (int page = 0; page <= (count + 3) / 4; page++)
            EqualJson(await repo.GetLastMatchesByUserId(userId, 4, page), history.Skip(page * 4).Take(4).ToList());
    }

    [Test]
    public async Task ParticipantIds_AreRefreshedAfterSoftDeleteOnTheSameRepository()
    {
        var userId = users["solo"];
        var repo = new MatchRepository(context, new DateTimeProvider(), null!, null!, null!);
        Assert.That((await repo.GetStatsByUserId(userId)).TotalMatches, Is.EqualTo(16));
        await context.Set<TournamentParticipantEntity>().Where(p => p.UserId == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.IsDeleted, true));
        Assert.That((await repo.GetStatsByUserId(userId)).TotalMatches, Is.EqualTo(2),
            "Only direct team-player matches should remain after all solo participations are deleted.");
    }

    private static bool IsHome(MatchEntity m, Guid userId) =>
        m.HomeUserId == userId || (m.TeamMatchId == null && m.HomeParticipant?.UserId == userId);

    private static string Outcome(MatchEntity m, Guid userId) => m.WinnerParticipantId == null ? "D"
        : m.WinnerParticipantId == (IsHome(m, userId) ? m.HomeParticipantId : m.AwayParticipantId) ? "W" : "L";

    private static MatchListItemDto HistoryItem(MatchEntity m, Guid userId)
    {
        bool home = IsHome(m, userId);
        var homeUser = m.HomeUser ?? m.HomeParticipant?.User;
        var awayUser = m.AwayUser ?? m.AwayParticipant?.User;
        var me = home ? homeUser : awayUser;
        var opponent = home ? awayUser : homeUser;
        return new MatchListItemDto
        {
            HubName = m.Tournament!.Hub!.Name,
            TournamentName = m.Tournament.Name,
            ScheduledTime = m.ScheduledStartTime,
            Username = me?.Username ?? "Unknown",
            UserAvatarUrl = me?.AvatarUrl,
            OpponentName = opponent?.Username ?? "Unknown",
            OpponentAvatarUrl = opponent?.AvatarUrl,
            UserScore = home ? m.HomeUserScore : m.AwayUserScore,
            OpponentScore = home ? m.AwayUserScore : m.HomeUserScore,
            IsWin = Outcome(m, userId) == "W"
        };
    }

    private static void EqualJson<T>(T actual, T expected) =>
        Assert.That(JsonSerializer.Serialize(actual), Is.EqualTo(JsonSerializer.Serialize(expected)));

    private async Task Seed()
    {
        var role = new UserRoleEntity { Id = Guid.NewGuid() };
        UserEntity User(string name)
        {
            var user = new UserEntity
            {
                Id = Guid.NewGuid(), UserRole = role, Username = name,
                Email = Guid.NewGuid() + "@example.invalid", AvatarUrl = name + ".png"
            };
            users[name] = user.Id.Value;
            context.Add(user);
            return user;
        }
        var soloUser = User("solo");
        var opponentUser = User("opponent");
        var teamUser = User("team-only");
        var deletedUser = User("deleted-participant");
        var unrelatedUser = User("unrelated");
        users["missing"] = Guid.NewGuid();
        var hub = new HubEntity { Id = Guid.NewGuid(), Name = "Profile hub", User = soloUser };
        var tournament = new TournamentEntity { Id = Guid.NewGuid(), Name = "Profile cup", Hub = hub };
        TournamentParticipantEntity Participant(UserEntity? user, bool deleted = false) => new()
        {
            Id = Guid.NewGuid(), Tournament = tournament, User = user, IsDeleted = deleted
        };
        var solo = Participant(soloUser);
        var secondSolo = Participant(soloUser);
        var opponent = Participant(opponentUser);
        var deletedSolo = Participant(soloUser, true);
        var deletedOpponent = Participant(deletedUser, true);
        var unrelated = Participant(unrelatedUser);
        var homeTeam = Participant(null);
        var awayTeam = Participant(null);
        var teamMatch = new TeamMatchEntity
        {
            Id = Guid.NewGuid(), Tournament = tournament, HomeTeamParticipant = homeTeam, AwayTeamParticipant = awayTeam
        };
        context.Add(teamMatch);
        int sequence = 0;
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        MatchEntity Match(TournamentParticipantEntity? home, TournamentParticipantEntity? away, int winner = 0)
        {
            sequence++;
            var match = new MatchEntity
            {
                Id = Guid.NewGuid(), Tournament = tournament, HomeParticipant = home, AwayParticipant = away,
                Status = MatchStatus.Completed, WinnerParticipant = winner == 1 ? home : winner == 2 ? away : null,
                ScheduledStartTime = start.AddHours(sequence), ModifiedOn = start.AddHours(200 - sequence),
                HomeUserScore = sequence, AwayUserScore = sequence + 1
            };
            context.Add(match);
            return match;
        }
        // More than ten outcomes, wins/losses/draws, both participant sides, multiple entries
        // for one user, and different ModifiedOn versus ScheduledStartTime ordering.
        for (int i = 0; i < 12; i++)
            Match(i % 2 == 0 ? solo : opponent, i % 2 == 0 ? opponent : secondSolo, i % 3);
        // A deleted opposing participant must not hide the live user's result.
        Match(solo, deletedOpponent, 1).ScheduledStartTime = null;
        // Both sides belonging to one user must still count as only one match.
        Match(solo, secondSolo, 1);
        // Team-only users have an empty solo participant list; direct user IDs still match.
        var teamHome = Match(homeTeam, awayTeam, 1);
        teamHome.TeamMatch = teamMatch;
        teamHome.HomeUser = teamUser;
        teamHome.AwayUser = soloUser;
        var teamAway = Match(null, null);
        teamAway.TeamMatch = teamMatch;
        teamAway.HomeUser = soloUser;
        teamAway.AwayUser = teamUser;

        Match(deletedSolo, deletedOpponent, 1); // Neither deleted participant can grant membership.
        Match(unrelated, unrelated, 1);
        Match(solo, opponent, 1).IsDeleted = true;
        Match(solo, opponent, 1).Status = MatchStatus.Pending;
        Match(solo, null, 1); // Solo bye/incomplete slot stays excluded.
        Match(null, solo, 2);
        var incompleteTeam = Match(homeTeam, awayTeam, 1);
        incompleteTeam.TeamMatch = teamMatch;
        incompleteTeam.HomeUser = soloUser; // Missing away user stays excluded.
        var otherIncompleteTeam = Match(homeTeam, awayTeam, 2);
        otherIncompleteTeam.TeamMatch = teamMatch;
        otherIncompleteTeam.AwayUser = soloUser;
        var strayDirectUser = Match(unrelated, unrelated);
        strayDirectUser.HomeUser = soloUser; // Direct users do not grant solo membership.
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
    }
}
