using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GameHubz.Data.Context;
using GameHubz.Data.Repository;
using GameHubz.DataModels.Domain;
using GameHubz.DataModels.Enums;
using GameHubz.Logic.Test.Bracket;
using GameHubz.Logic.Utility;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace GameHubz.Logic.Test.Repositories;

/// <summary>
/// The selection half of the retention sweep, run as real SQL. EvidenceRetentionServiceTests pins
/// what the sweep does with the rows it is handed; this pins WHICH rows it is handed — the part
/// that decides what gets permanently deleted, and the part a mocked repository cannot see.
/// </summary>
[TestFixture]
public sealed class EvidenceRetentionQueryTests
{
    private SqliteConnection connection = null!;
    private ApplicationContext context = null!;
    private DateTime now;

    private TournamentEntity live = null!;
    private TournamentEntity ended = null!;

    [SetUp]
    public async Task SetUp()
    {
        connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        context = new TestApplicationContext(new DbContextOptionsBuilder<TestApplicationContext>()
            .UseSqlite(connection).Options);
        await context.Database.EnsureCreatedAsync();

        now = DateTime.UtcNow;

        var role = new UserRoleEntity { Id = Guid.NewGuid() };
        var owner = new UserEntity { Id = Guid.NewGuid(), Username = "owner", UserRole = role };
        var hub = new HubEntity { Id = Guid.NewGuid(), User = owner };

        live = new TournamentEntity { Id = Guid.NewGuid(), Hub = hub };
        ended = new TournamentEntity { Id = Guid.NewGuid(), Hub = hub, EndedOn = now.AddDays(-10) };
        context.AddRange(live, ended);
        await context.SaveChangesAsync();
    }

    [TearDown]
    public async Task TearDown()
    {
        await context.DisposeAsync();
        await connection.DisposeAsync();
    }

    private MatchEvidenceRepository Repo()
        => (MatchEvidenceRepository)Activator.CreateInstance(
            typeof(MatchEvidenceRepository), context, new DateTimeProvider(), null, null, null)!;

    private MatchEntity Match(
        TournamentEntity tournament,
        MatchStatus status,
        int touchedDaysAgo,
        bool adminHelp = false,
        Guid? proposedBy = null)
    {
        var match = new MatchEntity
        {
            Id = Guid.NewGuid(),
            Tournament = tournament,
            Status = status,
            CreatedOn = now.AddDays(-60),
            ModifiedOn = now.AddDays(-touchedDaysAgo),
            AdminHelpRequested = adminHelp,
            ProposedByUserId = proposedBy,
        };
        context.Add(match);
        return match;
    }

    private MatchEvidenceEntity Evidence(MatchEntity match, EvidenceMediaType type, int ageDays, bool retired = false)
    {
        var evidence = new MatchEvidenceEntity
        {
            Id = Guid.NewGuid(),
            Match = match,
            MediaType = type,
            Url = "url",
            StorageKey = "key",
            CreatedOn = now.AddDays(-ageDays),
            IsDeleted = retired,
        };
        context.Add(evidence);
        return evidence;
    }

    private async Task<List<Guid>> ExpiredVideos(bool perMatchRule = true)
    {
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var rows = await Repo().GetExpiredVideos(
            matchSettledBefore: perMatchRule ? now.AddDays(-7) : DateTime.UnixEpoch,
            endedBefore: now.AddDays(-3),
            createdBefore: now.AddDays(-30),
            take: 100);

        return rows.Select(r => r.Id!.Value).ToList();
    }

    private async Task<List<Guid>> ExpiredImages()
    {
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var rows = await Repo().GetExpiredImages(
            endedBefore: now.AddDays(-7),
            createdBefore: now.AddDays(-30),
            take: 100);

        return rows.Select(r => r.Id!.Value).ToList();
    }

    // ---------------------------------------------------------------- clips: the per-match rule

    [Test]
    public async Task Clip_OfASettledMatchLeftAloneForTheWindow_Expires_WhileTheTournamentStillRuns()
    {
        var clip = Evidence(Match(live, MatchStatus.Completed, touchedDaysAgo: 10), EvidenceMediaType.Video, ageDays: 10);

        Assert.That(await ExpiredVideos(), Is.EqualTo(new[] { clip.Id!.Value }));
    }

    [Test]
    public async Task Clip_IsKept_WhileItsResultIsStillBeingArguedAbout()
    {
        // Every one of these is ten days old on a match in a live tournament; each has one reason
        // the per-match rule must leave it alone.
        Evidence(Match(live, MatchStatus.Completed, touchedDaysAgo: 10, adminHelp: true), EvidenceMediaType.Video, ageDays: 10);
        Evidence(Match(live, MatchStatus.Completed, touchedDaysAgo: 10, proposedBy: Guid.NewGuid()), EvidenceMediaType.Video, ageDays: 10);
        // Result edited two days ago: the clock restarts.
        Evidence(Match(live, MatchStatus.Completed, touchedDaysAgo: 2), EvidenceMediaType.Video, ageDays: 10);
        // Not settled yet.
        Evidence(Match(live, MatchStatus.Scheduled, touchedDaysAgo: 10), EvidenceMediaType.Video, ageDays: 10);
        // Attached yesterday to a long-settled match: somebody is disputing it now.
        Evidence(Match(live, MatchStatus.Completed, touchedDaysAgo: 10), EvidenceMediaType.Video, ageDays: 1);

        Assert.That(await ExpiredVideos(), Is.Empty);
    }

    [Test]
    public async Task Clip_PerMatchRuleSwitchedOff_LeavesOnlyTheOlderRules()
    {
        Evidence(Match(live, MatchStatus.Completed, touchedDaysAgo: 10), EvidenceMediaType.Video, ageDays: 10);
        var backstop = Evidence(Match(live, MatchStatus.Scheduled, touchedDaysAgo: 1), EvidenceMediaType.Video, ageDays: 40);
        var tournamentOver = Evidence(Match(ended, MatchStatus.Completed, touchedDaysAgo: 1), EvidenceMediaType.Video, ageDays: 12);

        Assert.That(
            await ExpiredVideos(perMatchRule: false),
            Is.EquivalentTo(new[] { backstop.Id!.Value, tournamentOver.Id!.Value }));
    }

    [Test]
    public async Task Clip_AdminHelp_DoesNotOutliveTheTournamentEndOrTheBackstop()
    {
        // The exemption belongs to the per-match rule only. A request nobody ever resolves must
        // not turn into a clip that is paid for forever.
        var tournamentOver = Evidence(Match(ended, MatchStatus.Completed, touchedDaysAgo: 10, adminHelp: true), EvidenceMediaType.Video, ageDays: 12);
        var backstop = Evidence(Match(live, MatchStatus.Completed, touchedDaysAgo: 40, adminHelp: true), EvidenceMediaType.Video, ageDays: 40);

        Assert.That(await ExpiredVideos(), Is.EquivalentTo(new[] { tournamentOver.Id!.Value, backstop.Id!.Value }));
    }

    // ---------------------------------------------------------------- screenshots

    [Test]
    public async Task Screenshot_IsKeptForTheWholeTournament_AndGoesAfterItEnds()
    {
        var settled = Match(live, MatchStatus.Completed, touchedDaysAgo: 10);
        Evidence(settled, EvidenceMediaType.Image, ageDays: 10);                 // live tournament: stays

        var finished = Match(ended, MatchStatus.Completed, touchedDaysAgo: 12);
        var gone = Evidence(finished, EvidenceMediaType.Image, ageDays: 12);     // ended + grace passed
        Evidence(finished, EvidenceMediaType.Image, ageDays: 1);                 // attached after the end: a dispute

        Assert.That(await ExpiredImages(), Is.EqualTo(new[] { gone.Id!.Value }));
    }

    [Test]
    public async Task Screenshot_OlderThanTheBackstop_GoesEvenWhileTheTournamentRuns()
    {
        var old = Evidence(Match(live, MatchStatus.Completed, touchedDaysAgo: 40), EvidenceMediaType.Image, ageDays: 40);

        Assert.That(await ExpiredImages(), Is.EqualTo(new[] { old.Id!.Value }));
    }

    [Test]
    public async Task EachQuery_OnlyReturnsItsOwnMediaType()
    {
        var match = Match(ended, MatchStatus.Completed, touchedDaysAgo: 12);
        var clip = Evidence(match, EvidenceMediaType.Video, ageDays: 12);
        var screenshot = Evidence(match, EvidenceMediaType.Image, ageDays: 12);

        Assert.That(await ExpiredVideos(), Is.EqualTo(new[] { clip.Id!.Value }));
        Assert.That(await ExpiredImages(), Is.EqualTo(new[] { screenshot.Id!.Value }));
    }

    // ---------------------------------------------------------------- "expired", not "never attached"

    [Test]
    public async Task ExpiredEvidenceLookup_FindsOnlyMatchesWhoseEvidenceWasRetired()
    {
        var retired = Match(live, MatchStatus.Completed, touchedDaysAgo: 10);
        Evidence(retired, EvidenceMediaType.Video, ageDays: 10, retired: true);

        var stillProven = Match(live, MatchStatus.Completed, touchedDaysAgo: 10);
        Evidence(stillProven, EvidenceMediaType.Image, ageDays: 10);

        var neverProven = Match(live, MatchStatus.Completed, touchedDaysAgo: 10);

        var otherTournament = Match(ended, MatchStatus.Completed, touchedDaysAgo: 12);
        Evidence(otherTournament, EvidenceMediaType.Image, ageDays: 12, retired: true);

        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var byTournament = await Repo().GetMatchIdsWithExpiredEvidenceByTournament(live.Id!.Value);
        var byIds = await Repo().GetMatchIdsWithExpiredEvidence(new[]
        {
            retired.Id!.Value, stillProven.Id!.Value, neverProven.Id!.Value, otherTournament.Id!.Value
        });

        Assert.Multiple(() =>
        {
            Assert.That(byTournament, Is.EquivalentTo(new[] { retired.Id!.Value }));
            Assert.That(byIds, Is.EquivalentTo(new[] { retired.Id!.Value, otherTournament.Id!.Value }));
        });
    }
}
