using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GameHubz.Data.Context;
using GameHubz.Api.Controllers;
using GameHubz.Api.Share;
using GameHubz.DataModels.Config;
using GameHubz.Data.Repository;
using GameHubz.DataModels.Domain;
using GameHubz.DataModels.Enums;
using GameHubz.DataModels.Models;
using GameHubz.Logic.Test.Bracket;
using GameHubz.Logic.Services;
using GameHubz.Logic.Utility;
using Microsoft.Data.Sqlite;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
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
    private CommandCounter commands = null!;

    [SetUp]
    public async Task SetUp()
    {
        commands = new CommandCounter();
        // Only an explicitly supplied disposable database is used for PostgreSQL tests.
        // Application connection strings are never read. Test rows are rolled back.
        var postgres = Environment.GetEnvironmentVariable("GAMEHUBZ_QUERY_TEST_POSTGRES");
        if (!string.IsNullOrEmpty(postgres))
        {
            context = new ApplicationContext(new DbContextOptionsBuilder<ApplicationContext>()
                .UseNpgsql(postgres).AddInterceptors(commands).Options);
        }
        else
        {
            connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            context = new TestApplicationContext(new DbContextOptionsBuilder<TestApplicationContext>()
                .UseSqlite(connection).AddInterceptors(commands).Options);
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

    [TestCase("solo", "opponent")]
    [TestCase("opponent", "solo")]
    [TestCase("solo", "team-only")]
    [TestCase("team-only", "solo")]
    [TestCase("solo", "deleted-participant")]
    [TestCase("solo", "missing")]
    [TestCase("missing", "team-only")]
    [TestCase("solo", "solo")]
    [TestCase("unrelated", "unrelated")]
    public async Task HeadToHead_PreservesLegacyResultsAndLastMeeting(string user, string opponent)
    {
        var userId = users[user];
        var opponentId = users[opponent];
        var legacy = await context.Set<MatchEntity>().AsNoTracking()
            .Where(m => m.Status == MatchStatus.Completed &&
                ((m.TeamMatchId == null && m.HomeParticipantId != null && m.AwayParticipantId != null &&
                    ((m.HomeParticipant!.UserId == userId && m.AwayParticipant!.UserId == opponentId)
                    || (m.HomeParticipant!.UserId == opponentId && m.AwayParticipant!.UserId == userId)))
                || (m.TeamMatchId != null && m.HomeUserId != null && m.AwayUserId != null &&
                    ((m.HomeUserId == userId && m.AwayUserId == opponentId)
                    || (m.HomeUserId == opponentId && m.AwayUserId == userId)))))
            .OrderByDescending(m => m.ScheduledStartTime ?? m.ModifiedOn)
            .Include(m => m.HomeParticipant)
            .Include(m => m.Tournament).ThenInclude(t => t!.Hub)
            .ToListAsync();
        var expected = new HeadToHeadDto { TotalMatches = legacy.Count };
        foreach (var match in legacy)
        {
            bool home = (match.HomeUserId ?? match.HomeParticipant?.UserId) == userId;
            string outcome = match.WinnerParticipantId == null ? "D"
                : match.WinnerParticipantId == (home ? match.HomeParticipantId : match.AwayParticipantId) ? "W" : "L";
            if (outcome == "W") expected.MyWins++;
            else if (outcome == "L") expected.OpponentWins++;
            else expected.Draws++;
            if (expected.LastOutcome != null) continue;
            expected.LastOutcome = outcome;
            expected.LastMatchTime = match.ScheduledStartTime ?? match.ModifiedOn;
            expected.LastMyScore = home ? match.HomeUserScore : match.AwayUserScore;
            expected.LastOpponentScore = home ? match.AwayUserScore : match.HomeUserScore;
            expected.LastTournamentName = match.Tournament!.Name;
            expected.LastHubName = match.Tournament.Hub!.Name;
        }
        var repo = new MatchRepository(context, new DateTimeProvider(), null!, null!, null!);
        EqualJson(await repo.GetHeadToHead(userId, opponentId), expected);
    }

    [TestCase("solo")]
    [TestCase("opponent")]
    [TestCase("team-only")]
    [TestCase("deleted-participant")]
    [TestCase("missing")]
    public async Task ProfileV2_PreservesStatsFormAndStreakWithFourQueriesAndCachedReads(string user)
    {
        var userId = users[user];
        var factory = new TestUnitOfWorkFactory(context, null!);
        var uow = factory.CreateAppUnitOfWork();
        var expectedStats = await uow.MatchRepository.GetStatsByUserId(userId);
        expectedStats.TournamentsWon = await uow.TournamentRepository.GetNumberOfTournamentsWonByUserId(userId);
        expectedStats.TournamentsPlayed = await uow.TournamentParticipantRepository.CountTournamentsByUserId(userId);
        var outcomes = await uow.MatchRepository.GetOutcomesByUserId(userId);
        expectedStats.LongestWinStreak = string.Concat(outcomes).Split('D', 'L').Max(run => run.Length);
        var service = new UserProfileService(null!, factory, null!, null!, new FakeCacheService(), null!);
        commands.Count = 0;
        var actual = await service.GetStatsV2(userId);
        EqualJson(actual.Stats, expectedStats);
        Assert.That(actual.Performance.Select(p => p.Outcome), Is.EqualTo(outcomes.Take(10)));
        Assert.That(commands.Count, Is.EqualTo(4), "An uncached profile should read its match history only once.");
        commands.Count = 0;
        EqualJson(await service.GetStatsV2(userId), actual);
        Assert.That(commands.Count, Is.Zero, "A cached profile should not query the database.");
    }

    [Test]
    public async Task UnreadCounts_PreserveReadBoundariesMissingAndDeletedCursorsAndMessageFilters()
    {
        var userId = users["solo"];
        var authorId = users["opponent"];
        var matches = await context.Set<MatchEntity>().OrderBy(m => m.ScheduledStartTime).Take(5).ToListAsync();
        var at = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);
        void Message(int match, Guid? author, DateTime? time, bool deleted = false) => context.Add(new MatchChatEntity
        {
            Id = Guid.NewGuid(), MatchId = matches[match].Id, UserId = author,
            CreatedOn = time, Content = "test", IsDeleted = deleted
        });
        void Cursor(int match, Guid user, bool deleted = false, bool muted = false) => context.Add(new MatchChatReadEntity
        {
            Id = Guid.NewGuid(), MatchId = matches[match].Id!.Value, UserId = user,
            LastReadAt = at, IsDeleted = deleted, IsMuted = muted
        });
        Cursor(0, userId);
        Cursor(1, authorId); // Another reader's cursor must not affect this user.
        Cursor(2, userId, deleted: true);
        Cursor(3, userId, muted: true); // Mute is handled by the caller, not by this count query.
        for (int i = 0; i < 5; i++)
        {
            Message(i, authorId, at.AddSeconds(-1));
            Message(i, authorId, at);
            Message(i, authorId, at.AddSeconds(1));
            Message(i, userId, at.AddSeconds(1));
            Message(i, null, at.AddSeconds(1));
            Message(i, authorId, null);
            Message(i, authorId, at.AddSeconds(1), deleted: true);
        }
        context.Add(new MatchChatEntity { Id = Guid.NewGuid(), UserId = authorId, CreatedOn = at, Content = "no match" });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var ids = matches.Take(4).Select(m => m.Id!.Value).ToList();
        ids.Add(ids[0]); // Duplicate input IDs must not multiply counts.
        ids.Add(Guid.NewGuid());
        var legacy = await context.Set<MatchChatEntity>().AsNoTracking()
            .Where(mc => mc.MatchId != null && ids.Contains(mc.MatchId.Value) && mc.UserId != userId)
            .Select(mc => new
            {
                MatchId = mc.MatchId!.Value, mc.CreatedOn,
                LastRead = context.Set<MatchChatReadEntity>()
                    .Where(r => r.MatchId == mc.MatchId!.Value && r.UserId == userId)
                    .Select(r => (DateTime?)r.LastReadAt).FirstOrDefault()
            })
            .Where(x => x.LastRead == null || x.CreatedOn > x.LastRead)
            .GroupBy(x => x.MatchId).Select(g => new { Id = g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Id, x => x.Count);
        var repo = new MatchChatRepository(context, new DateTimeProvider(), null!, null!, null!);
        var actual = await repo.GetUnreadCountsByMatch(ids, userId);
        Assert.That(actual, Is.EquivalentTo(legacy));
        Assert.That(actual[matches[0].Id!.Value], Is.EqualTo(2));
        Assert.That(actual[matches[1].Id!.Value], Is.EqualTo(5));
        Assert.That(actual[matches[2].Id!.Value], Is.EqualTo(5));
        Assert.That(actual[matches[3].Id!.Value], Is.EqualTo(2));
        commands.Count = 0;
        Assert.That(await repo.GetUnreadCountsByMatch(new List<Guid>(), userId), Is.Empty);
        Assert.That(commands.Count, Is.Zero);
    }

    [TestCase("solo")]
    [TestCase("team-only")]
    [TestCase("missing")]
    public async Task TournamentMembership_PreservesLegacyCountPagesAndSoftDeleteRules(string user)
    {
        var hub = await context.Set<HubEntity>().SingleAsync();
        for (int i = 0; i < 7; i++)
        {
            var tournament = new TournamentEntity { Id = Guid.NewGuid(), Hub = hub, Name = "Membership " + i,
                StartDate = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(i) };
            var team = new TournamentTeamEntity { Id = Guid.NewGuid(), Tournament = tournament, IsDeleted = i == 2 };
            context.Add(new TournamentParticipantEntity { Id = Guid.NewGuid(), Tournament = tournament,
                Team = team, UserId = i == 0 ? users["solo"] : null, IsDeleted = i == 4 });
            context.Add(new TournamentTeamMemberEntity { Id = Guid.NewGuid(), Team = team,
                UserId = users[i == 1 ? "team-only" : i == 5 ? "unrelated" : "solo"],
                IsDeleted = i == 3, IsReserve = i == 1 });
        }
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var id = users[user];
        var legacy = context.Set<TournamentParticipantEntity>().AsNoTracking()
            .Where(p => p.UserId == id || (p.TeamId != null && p.Team!.Members.Any(m => m.UserId == id)));
        var repo = new TournamentParticipantRepository(context, new DateTimeProvider(), null!, null!, null!);
        Assert.That(await repo.CountTournamentsByUserId(id), Is.EqualTo(await legacy.Select(p => p.TournamentId).Distinct().CountAsync()));
        var count = await legacy.CountAsync();
        for (int page = 0; page <= count / 2 + 1; page++)
        {
            var expected = await legacy.OrderByDescending(p => p.Tournament!.StartDate).Skip(page * 2).Take(2)
                .Select(p => new { Id = p.Tournament!.Id!.Value, p.Tournament.Name,
                    StartDate = p.Tournament.StartDate ?? DateTime.MinValue,
                    Participants = p.Tournament.TournamentParticipants!.Count() }).ToListAsync();
            var actual = await repo.GetByUserIdPaged(id, page, 2);
            Assert.That(actual.Count, Is.EqualTo(count));
            EqualJson(actual.Items.Select(t => new { t.Id, t.Name, t.StartDate, Participants = t.NumberOfParticipants }).ToList(), expected);
        }
        // No stale membership cache on a reused repository instance.
        await context.Set<TournamentTeamMemberEntity>().Where(m => m.UserId == id)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.IsDeleted, true));
        Assert.That(await repo.CountTournamentsByUserId(id), Is.EqualTo(await legacy.Select(p => p.TournamentId).Distinct().CountAsync()));
    }

    [TestCase("solo")]
    [TestCase("team-only")]
    [TestCase("deleted-participant")]
    public async Task ShareCard_PreservesProfileNumbersHtmlAndCache(string user)
    {
        var id = users[user];
        await context.Set<UserEntity>().Where(u => u.Id == id).ExecuteUpdateAsync(s => s.SetProperty(u => u.IsActive, true));
        var factory = new TestUnitOfWorkFactory(context, null!);
        var stats = await factory.CreateAppUnitOfWork().MatchRepository.GetStatsByUserId(id);
        var config = new ShareLinksConfig();
        int draws = stats.TotalMatches - stats.Wins - stats.Losses;
        int rate = stats.TotalMatches > 0 ? (int)Math.Round(stats.Wins * 100.0 / stats.TotalMatches) : 0;
        string description = stats.TotalMatches > 0
            ? $"{stats.TotalMatches} matches · {stats.Wins}W {stats.Losses}L {draws}D · {rate}% win rate · 0 trophies on {config.AppName}"
            : $"Player profile on {config.AppName} — tournaments, match history and stats.";
        var expected = SharePageBuilder.BuildPage(new SharePageModel
        {
            Title = user, Description = description, CanonicalUrl = $"{config.BaseUrl.TrimEnd('/')}/user/{id}",
            DeepLink = $"{config.AppScheme}://player/{id}", EntityLabel = "Player", ImageUrl = user + ".png",
            AppName = config.AppName, AppStoreUrl = config.AppStoreUrl, PlayStoreUrl = config.PlayStoreUrl,
            Scoreboard = new PlayerScoreboard(stats.TotalMatches, rate, stats.Wins, draws, stats.Losses, 0)
        });
        var cache = new FakeCacheService();
        // Keep analytics outside the query-count assertion.
        await cache.SetAsync($"sharelog_ip:unknown:{DateTime.UtcNow:yyyyMMdd}", 200);
        var controller = new ShareController(context, Options.Create(config), NullLogger<ShareController>.Instance, cache,
            factory.CreateAppUnitOfWork().MatchRepository)
            { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
        Assert.That((await controller.UserProfile(id)).Content, Is.EqualTo(expected));
        commands.Count = 0;
        Assert.That((await controller.UserProfile(id)).Content, Is.EqualTo(expected));
        Assert.That(commands.Count, Is.Zero);
        Assert.That((await controller.UserProfile(users["missing"])).StatusCode, Is.EqualTo(404));
    }

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
