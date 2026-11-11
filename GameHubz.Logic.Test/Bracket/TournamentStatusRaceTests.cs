using System;
using System.Data.Common;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GameHubz.Data.Context;
using GameHubz.DataModels.Config;
using GameHubz.DataModels.Domain;
using GameHubz.DataModels.Enums;
using GameHubz.DataModels.Models;
using GameHubz.Logic.Exceptions;
using GameHubz.Logic.Interfaces;
using GameHubz.Logic.Services;
using GameHubz.Logic.SignalR;
using GameHubz.Logic.Test.Factories;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;

namespace GameHubz.Logic.Test.Bracket;

// Lifecycle transitions (open / cancel / delete, TournamentService.ChangeTournamentStatus), the start
// and tournament edits racing each other. SQLite gates hold one request at a chosen point while the
// other one commits, so the ordering is forced around real service writes; PostgreSQL's own locking
// is not exercised here.
[TestFixture]
public class TournamentStatusRaceTests
{
    [Test]
    public async Task OpenRegistrationLoadedBeforeStart_IsRefused_AndTheStartStands()
    {
        var h = new BracketTestHarness(useSqlite: true);
        var tid = await h.SeedSoloTournamentAsync(TournamentFormat.League, 8);
        SetStatus(h, tid, TournamentStatus.RegistrationClosed);

        var gate = new TransactionGate();
        using var intercepted = Intercepted(h, gate);
        var open = h.NewLifecycleTournamentService(intercepted).OpenRegistration(tid);
        try
        {
            await Task.WhenAny(gate.Entered.Task, open).WaitAsync(TimeSpan.FromSeconds(15));
            if (open.IsCompleted) await open;
            Assert.That(gate.Entered.Task.IsCompleted, Is.True, "open must be held at its transaction");

            await h.NewService().CreateBracket(new CreateBracketRequest { TournamentId = tid });
            Assert.That(h.Tournament(tid).Status, Is.EqualTo(TournamentStatus.InProgress));
        }
        finally { gate.Resume.TrySetResult(); }

        Assert.That(async () => await open.WaitAsync(TimeSpan.FromSeconds(15)), Throws.InstanceOf<BusinessRuleException>(),
            "the open read the closed status before the start; on fresh data it must refuse");
        Assert.That(h.Tournament(tid).Status, Is.EqualTo(TournamentStatus.InProgress),
            "a started tournament must not be turned back into 'registration open'");
    }

    [Test]
    public async Task ResultLoadedBeforeCancel_IsRefused_AndTheTournamentStaysCancelled()
    {
        var h = new BracketTestHarness(useSqlite: true);
        var tid = await h.SeedSoloTournamentAsync(TournamentFormat.League, 4);
        await h.NewService().GenerateLeagueTournament(tid);
        var match = h.Matches(tid).First(m => m.RoundOpenAt == null || m.RoundOpenAt <= DateTime.UtcNow);

        var gate = new ResultGate();
        using var intercepted = Intercepted(h, gate);
        var report = ResultService(h, intercepted).UpdateMatchResult(new MatchResultDto
        {
            MatchId = match.Id!.Value, TournamentId = tid, HomeScore = 2, AwayScore = 0,
        });
        try
        {
            await Task.WhenAny(gate.Entered.Task, report).WaitAsync(TimeSpan.FromSeconds(15));
            if (report.IsCompleted) await report;
            Assert.That(gate.Entered.Task.IsCompleted, Is.True, "the report must be held at the advancement lock");

            await h.NewLifecycleTournamentService().CancelTournament(tid);
            Assert.That(h.Tournament(tid).Status, Is.EqualTo(TournamentStatus.Cancelled));
        }
        finally { gate.Resume.TrySetResult(); }

        Assert.That(async () => await report.WaitAsync(TimeSpan.FromSeconds(15)), Throws.InstanceOf<BusinessRuleException>(),
            "a result that waited for the cancel must be refused");
        Assert.Multiple(() =>
        {
            Assert.That(h.Tournament(tid).Status, Is.EqualTo(TournamentStatus.Cancelled));
            Assert.That(h.Match(match.Id.Value).Status, Is.Not.EqualTo(MatchStatus.Completed));
        });
    }

    [Test]
    public async Task ResultOnACancelledTournament_IsRefused()
    {
        var h = new BracketTestHarness(useSqlite: true);
        var tid = await h.SeedSoloTournamentAsync(TournamentFormat.League, 4);
        await h.NewService().GenerateLeagueTournament(tid);
        await h.NewLifecycleTournamentService().CancelTournament(tid);
        var match = h.Matches(tid).First(m => m.RoundOpenAt == null || m.RoundOpenAt <= DateTime.UtcNow);

        Assert.That(async () => await h.NewService().UpdateMatchResult(new MatchResultDto
        {
            MatchId = match.Id!.Value, TournamentId = tid, HomeScore = 1, AwayScore = 0,
        }), Throws.InstanceOf<BusinessRuleException>());
        Assert.That(h.Tournament(tid).Status, Is.EqualTo(TournamentStatus.Cancelled));
    }

    [Test]
    public async Task EditFromAStaleForm_DoesNotRewriteTheStatus()
    {
        var h = new BracketTestHarness(useSqlite: true);
        var tid = await h.SeedSoloTournamentAsync(TournamentFormat.League, 8);
        var seeded = h.Tournament(tid);
        Assert.That(seeded.Status, Is.EqualTo(TournamentStatus.InProgress));

        // The edit form was opened while registration was open and still says so.
        await h.NewTournamentService().SaveEntity(new TournamentPost
        {
            Id = tid,
            HubId = seeded.HubId,
            Name = "Renamed cup",
            Status = TournamentStatus.RegistrationOpen,
            MaxPlayers = seeded.MaxPlayers ?? 8,
            Format = seeded.Format,
            Region = seeded.Region,
        });

        var after = h.Tournament(tid);
        Assert.Multiple(() =>
        {
            Assert.That(after.Name, Is.EqualTo("Renamed cup"), "the edit itself must go through");
            Assert.That(after.Status, Is.EqualTo(TournamentStatus.InProgress), "an edit must never move the lifecycle");
        });
    }

    [Test]
    public async Task DeletedTournament_CannotBeStarted()
    {
        var h = new BracketTestHarness(useSqlite: true);
        var tid = await h.SeedSoloTournamentAsync(TournamentFormat.League, 8);
        SetStatus(h, tid, TournamentStatus.Deleted);

        Assert.That(async () => await h.NewService().CreateBracket(new CreateBracketRequest { TournamentId = tid }),
            Throws.InstanceOf<BusinessRuleException>());
        Assert.That(h.Tournament(tid).Status, Is.EqualTo(TournamentStatus.Deleted));
    }

    private static void SetStatus(BracketTestHarness h, Guid tournamentId, TournamentStatus status)
    {
        using var ctx = h.ReadContext();
        ctx.Set<TournamentEntity>().Single(t => t.Id == tournamentId).Status = status;
        ctx.SaveChanges();
    }

    private static BracketService ResultService(BracketTestHarness h, ApplicationContext context)
    {
        var loc = new LocalizationServiceFactory().CreateService();
        var factory = new TestUnitOfWorkFactory(context, loc);
        var auth = new TournamentAuthorizationService(factory, h.UserContext, loc, h.Cache, null!);
        var badge = new BadgeService(factory, h.UserContext, loc, Mock.Of<IHubContext<UserHub>>(),
            new BadgeRefreshQueue(Mock.Of<IServiceScopeFactory>(), NullLogger<BadgeRefreshQueue>.Instance));
        return new BracketService(factory, h.UserContext, loc, null!, h.Cache, h.Notifications,
            auth, badge, null!, null!, null!, Mock.Of<IDiscordDmService>(),
            Options.Create(new ShareLinksConfig()), null!);
    }

    private static TestApplicationContext Intercepted(BracketTestHarness h, IInterceptor interceptor)
    {
        using var context = h.ReadContext();
        return new TestApplicationContext(new DbContextOptionsBuilder<TestApplicationContext>()
            .UseSqlite(context.Database.GetDbConnection()).AddInterceptors(interceptor).Options);
    }

    // Holds the request at its first advancement-lock call.
    private sealed class ResultGate : DbCommandInterceptor
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Resume { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("pg_advisory_lock("))
            {
                Entered.TrySetResult();
                await Resume.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
            }
            return result;
        }
    }

    // Holds the request at the start of its transaction.
    private sealed class TransactionGate : DbTransactionInterceptor
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Resume { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(DbConnection connection,
            TransactionStartingEventData eventData, InterceptionResult<DbTransaction> result,
            CancellationToken cancellationToken = default)
        {
            Entered.TrySetResult();
            await Resume.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
            return result;
        }
    }
}
