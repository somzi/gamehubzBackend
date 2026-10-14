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
using GameHubz.Logic.Interfaces;
using GameHubz.Logic.Exceptions;
using GameHubz.Logic.Services;
using GameHubz.Logic.SignalR;
using GameHubz.Logic.Test.Factories;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using NUnit.Framework;

namespace GameHubz.Logic.Test.Bracket;

// Races between a participant swap / lifecycle change and a request that loaded its data before it.
// SQLite gates hold one request at a chosen point (the advancement lock, or its transaction start)
// while the other one commits, so the ordering is forced around real service writes. PostgreSQL's
// own locking is not exercised here.
[TestFixture]
public class ParticipantSwapRaceTests
{
    [TestCase(true)]
    [TestCase(false)]
    public async Task ResultEditLoadedBeforeSwap_MustPreserveStampedPlayers(bool previouslyPlayed)
    {
        var h = new BracketTestHarness(useSqlite: true);
        var tid = await h.SeedSoloTournamentAsync(TournamentFormat.League, 8);
        await h.NewService().GenerateLeagueTournament(tid);
        var match = h.Matches(tid).First(m => m.RoundOpenAt == null || m.RoundOpenAt <= DateTime.UtcNow);
        if (previouslyPlayed) await h.NewService().UpdateMatchResult(new MatchResultDto
        {
            MatchId = match.Id!.Value, TournamentId = tid, HomeScore = 2, AwayScore = 0,
        });
        var outgoing = h.ParticipantUserId(match.HomeParticipantId!.Value);
        var incoming = Guid.NewGuid();
        using (var seed = h.ReadContext())
        {
            var tournament = seed.Set<TournamentEntity>().Single(t => t.Id == tid);
            seed.Add(new UserEntity { Id = incoming, Username = "incoming", Email = "incoming@example.invalid", Region = tournament.Region,
                IsActive = true, UserRole = new UserRoleEntity { Id = Guid.NewGuid() } });
            seed.Add(new UserHubEntity { Id = Guid.NewGuid(), UserId = incoming, HubId = tournament.HubId, HubRole = HubRole.HubExclusive });
            await seed.SaveChangesAsync();
        }
        var gate = new ResultGate();
        using var intercepted = Intercepted(h, gate);
        var loc = new LocalizationServiceFactory().CreateService();
        var factory = new TestUnitOfWorkFactory(intercepted, loc);
        var auth = new TournamentAuthorizationService(factory, h.UserContext, loc, h.Cache, null!);
        var service = new BracketService(factory, h.UserContext, loc, null!, h.Cache, h.Notifications,
            auth, Badge(h, factory, loc), null!, null!, null!, Mock.Of<IDiscordDmService>(),
            Options.Create(new ShareLinksConfig()), null!);
        var edit = service.UpdateMatchResult(new MatchResultDto
        {
            MatchId = match.Id.Value, TournamentId = tid, HomeScore = 0, AwayScore = 3,
        });
        try
        {
            await Task.WhenAny(gate.Entered.Task, edit).WaitAsync(TimeSpan.FromSeconds(15));
            if (edit.IsCompleted) await edit;
            Assert.That(gate.Entered.Task.IsCompleted, Is.True);
            using var swapContext = h.ReadContext();
            var swapFactory = new TestUnitOfWorkFactory(swapContext, loc);
            var swapAuth = new TournamentAuthorizationService(swapFactory, h.UserContext, loc, h.Cache, null!);
            var swap = new TournamentParticipantService(swapFactory, null!, loc, null!, null!, null!, h.UserContext,
                h.Cache, swapAuth, Badge(h, swapFactory, loc), h.Notifications, new ConfigurationBuilder().Build());
            await swap.SwapParticipant(tid, new ParticipantSwapRequest { OutgoingUserId = outgoing, IncomingUserId = incoming });
            if (previouslyPlayed) Assert.That(h.Match(match.Id.Value).HomeUserId, Is.EqualTo(outgoing), "swap stamped original player");
        }
        finally { gate.Resume.TrySetResult(); }
        try { await edit.WaitAsync(TimeSpan.FromSeconds(15)); }
        catch (BusinessRuleException) { /* A safe refusal/retry is also an acceptable fix. */ }
        Assert.Multiple(() =>
        {
            if (previouslyPlayed) Assert.That(h.Match(match.Id.Value).HomeUserId, Is.EqualTo(outgoing), "stale result edit must not erase the historical owner");
            else Assert.That(h.Match(match.Id.Value).Status, Is.Not.EqualTo(MatchStatus.Completed), "a report loaded for the outgoing player must not give the incoming player a loss");
            Assert.That(h.ParticipantUserId(match.HomeParticipantId.Value), Is.EqualTo(incoming), "the completed swap must remain effective");
        });
    }

    [Test]
    public async Task CloseLoadedBeforeBracketStart_MustNotRestoreRegistrationClosed()
    {
        var h = new BracketTestHarness(useSqlite: true);
        var tid = await h.SeedSoloTournamentAsync(TournamentFormat.League, 8);
        using (var seed = h.ReadContext())
        {
            seed.Set<TournamentEntity>().Single(t => t.Id == tid).Status = TournamentStatus.RegistrationOpen;
            await seed.SaveChangesAsync();
        }
        var gate = new TransactionGate();
        using var intercepted = Intercepted(h, gate);
        var loc = new LocalizationServiceFactory().CreateService();
        var factory = new TestUnitOfWorkFactory(intercepted, loc);
        var notifier = new TournamentNotifier(factory, h.Notifications, Mock.Of<IDiscordNotificationService>());
        var service = new TournamentService(factory, null!, loc, null!, null!, null!, h.UserContext,
            null!, h.Cache, notifier,
            new TournamentAuthorizationService(factory, h.UserContext, loc, h.Cache, null!), null!, null!);
        var close = service.CloseRegistration(tid);
        try
        {
            await Task.WhenAny(gate.Entered.Task, close).WaitAsync(TimeSpan.FromSeconds(15));
            if (close.IsCompleted) await close;
            Assert.That(gate.Entered.Task.IsCompleted, Is.True);
            await h.NewService().CreateBracket(new CreateBracketRequest { TournamentId = tid });
            Assert.That(h.Tournament(tid).Status, Is.EqualTo(TournamentStatus.InProgress));
        }
        finally { gate.Resume.TrySetResult(); }
        try { await close.WaitAsync(TimeSpan.FromSeconds(15)); }
        catch (BusinessRuleException) { /* Refusing a close after the start preserves the invariant. */ }
        Assert.That(h.Tournament(tid).Status, Is.EqualTo(TournamentStatus.InProgress),
            "a close request that waited for the start must not overwrite the started status");
    }

    private static TestApplicationContext Intercepted(BracketTestHarness h, IInterceptor interceptor)
    {
        using var context = h.ReadContext();
        return new TestApplicationContext(new DbContextOptionsBuilder<TestApplicationContext>()
            .UseSqlite(context.Database.GetDbConnection()).AddInterceptors(interceptor).Options);
    }

    private static BadgeService Badge(BracketTestHarness h, IUnitOfWorkFactory factory, ILocalizationService loc)
        => new(factory, h.UserContext, loc, Mock.Of<IHubContext<UserHub>>(),
            new BadgeRefreshQueue(Mock.Of<IServiceScopeFactory>(), NullLogger<BadgeRefreshQueue>.Instance));

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
