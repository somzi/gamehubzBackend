using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using GameHubz.DataModels.Models;
using GameHubz.Logic.Interfaces;
using GameHubz.Logic.Services;
using GameHubz.Logic.SignalR;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;

namespace GameHubz.Logic.Test.Services;

[TestFixture]
public sealed class BadgeRefreshQueueTests
{
    [Test]
    public async Task PushReturnsBeforeDatabaseWork_AndQueuedDuplicatesAreComputedOnce()
    {
        await using var harness = new Harness();
        var user = Guid.NewGuid();
        using (var request = harness.Provider.CreateScope())
        {
            var service = request.ServiceProvider.GetRequiredService<BadgeService>();
            for (int i = 0; i < 100; i++) Assert.That(service.PushAsync(user).IsCompletedSuccessfully, Is.True);
            Assert.That(harness.Calls, Is.Empty, "The triggering request must not compute badges.");
        }
        await harness.Queue.StartAsync(CancellationToken.None);
        var delivered = await harness.Next();
        Assert.That(delivered.Group, Is.EqualTo(UserHub.GroupName(user)));
        Assert.That(delivered.Badges.FriendRequests, Is.EqualTo(3));
        await harness.Queue.StopAsync(CancellationToken.None);
        Assert.That(harness.Calls.Count, Is.EqualTo(1));
        Assert.That(harness.DisposedScopes, Does.Contain(harness.Calls.Single().Scope));
        Assert.That(harness.DisposedUnitsOfWork, Is.EquivalentTo(harness.Calls.Select(x => x.Scope)));
        Assert.That(harness.Provider.GetServices<IHostedService>().OfType<BadgeRefreshQueue>().Single(),
            Is.SameAs(harness.Queue), "The hosted worker must drain the exact queue injected into requests.");
    }

    [Test]
    public async Task ChangeWhileSending_GetsAnotherFreshScope_AndCannotOvertakeTheFirstSend()
    {
        await using var harness = new Harness();
        var firstSending = Signal();
        var releaseFirst = Signal();
        int value = 1;
        harness.ReadCount = _ => Task.FromResult(value);
        harness.BeforeSend = async dto =>
        {
            if (dto.FriendRequests == 1) { firstSending.SetResult(); await releaseFirst.Task; }
        };
        var user = Guid.NewGuid();
        await harness.Queue.StartAsync(CancellationToken.None);
        try
        {
            harness.Queue.EnqueueUser(user);
            await firstSending.Task.WaitAsync(TimeSpan.FromSeconds(5));
            value = 2;
            Parallel.For(0, 100, _ => harness.Queue.EnqueueUser(user));
            Assert.That(harness.Calls.Count, Is.EqualTo(1), "A newer compute/send cannot overtake an in-flight send.");
            releaseFirst.SetResult();
            Assert.That((await harness.Next()).Badges.FriendRequests, Is.EqualTo(1));
            Assert.That((await harness.Next()).Badges.FriendRequests, Is.EqualTo(2));
            await harness.Queue.StopAsync(CancellationToken.None);
            Assert.That(harness.Calls.Count, Is.EqualTo(2));
            Assert.That(harness.Calls.Select(x => x.Scope).Distinct().Count(), Is.EqualTo(2));
            Assert.That(harness.DisposedScopes, Is.EquivalentTo(harness.Calls.Select(x => x.Scope)));
            Assert.That(harness.DisposedUnitsOfWork, Is.EquivalentTo(harness.Calls.Select(x => x.Scope)));
        }
        finally { releaseFirst.TrySetResult(); }
    }

    [Test]
    public async Task FailedRefresh_DoesNotPreventAnotherRefreshForThatUser()
    {
        await using var harness = new Harness();
        var failed = Signal();
        int calls = 0;
        harness.ReadCount = _ =>
        {
            if (Interlocked.Increment(ref calls) == 1) { failed.SetResult(); throw new InvalidOperationException("test failure"); }
            return Task.FromResult(8);
        };
        var user = Guid.NewGuid();
        await harness.Queue.StartAsync(CancellationToken.None);
        harness.Queue.EnqueueUser(user);
        await failed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        harness.Queue.EnqueueUser(user);
        Assert.That((await harness.Next()).Badges.FriendRequests, Is.EqualTo(8));
        await harness.Queue.StopAsync(CancellationToken.None);
        Assert.That(harness.Calls.Count, Is.EqualTo(2));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task ManagersIncludeOwnerAndAdminsOnce_AndMissingTournamentDoesNothing(bool missing)
    {
        await using var harness = new Harness();
        var owner = Guid.NewGuid();
        var admin = Guid.NewGuid();
        harness.Ownership = missing ? null : new HubOwnershipInfo { HubId = Guid.NewGuid(), OwnerUserId = owner };
        harness.Managers = new List<Guid> { owner, admin, admin };
        harness.Queue.EnqueueTournamentManagers(new Guid(1, 0, 0, new byte[8]));
        // Same partition sentinel proves the missing-tournament job has been processed too.
        var sentinel = new Guid(5, 0, 0, new byte[8]);
        harness.Queue.EnqueueUser(sentinel);
        await harness.Queue.StartAsync(CancellationToken.None);
        var groups = new List<string>();
        for (int i = 0; i < (missing ? 1 : 3); i++) groups.Add((await harness.Next()).Group);
        await harness.Queue.StopAsync(CancellationToken.None);
        Assert.That(groups, Is.EquivalentTo((missing ? new[] { sentinel } : new[] { sentinel, owner, admin }).Select(UserHub.GroupName)));
    }

    [Test]
    public async Task FanOutHasAtMostFourConcurrentComputations()
    {
        await using var harness = new Harness();
        var fourStarted = Signal();
        var release = Signal();
        int running = 0, peak = 0;
        harness.ReadCount = async _ =>
        {
            int current = Interlocked.Increment(ref running);
            int seen;
            do { seen = peak; } while (current > seen && Interlocked.CompareExchange(ref peak, current, seen) != seen);
            if (current == 4) fourStarted.TrySetResult();
            await release.Task;
            Interlocked.Decrement(ref running);
            return 1;
        };
        for (int i = 1; i <= 32; i++) harness.Queue.EnqueueUser(new Guid(i, 0, 0, new byte[8]));
        await harness.Queue.StartAsync(CancellationToken.None);
        try
        {
            await fourStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.That(harness.Calls.Count, Is.EqualTo(4));
            release.SetResult();
            for (int i = 0; i < 32; i++) await harness.Next();
            Assert.That(peak, Is.EqualTo(4));
        }
        finally { release.TrySetResult(); }
    }

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class Harness : IAsyncDisposable
    {
        public ServiceProvider Provider { get; }
        public BadgeRefreshQueue Queue => Provider.GetRequiredService<BadgeRefreshQueue>();
        public Func<Guid, Task<int>> ReadCount { get; set; } = _ => Task.FromResult(3);
        public Func<BadgeCountsDto, Task> BeforeSend { get; set; } = _ => Task.CompletedTask;
        public HubOwnershipInfo? Ownership { get; set; }
        public List<Guid> Managers { get; set; } = new();
        public ConcurrentQueue<(Guid User, Guid Scope)> Calls { get; } = new();
        public ConcurrentBag<Guid> DisposedScopes { get; } = new();
        public ConcurrentBag<Guid> DisposedUnitsOfWork { get; } = new();
        private readonly Channel<(string Group, BadgeCountsDto Badges)> sent = Channel.CreateUnbounded<(string, BadgeCountsDto)>();

        public Harness()
        {
            var clients = new Mock<IHubClients>();
            clients.Setup(c => c.Group(It.IsAny<string>())).Returns((string group) =>
            {
                var proxy = new Mock<IClientProxy>();
                proxy.Setup(p => p.SendCoreAsync("BadgesUpdated", It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
                    .Returns(async (string _, object[] args, CancellationToken _) =>
                    {
                        var dto = (BadgeCountsDto)args[0];
                        await BeforeSend(dto);
                        await sent.Writer.WriteAsync((group, dto));
                    });
                return proxy.Object;
            });
            var hub = new Mock<IHubContext<UserHub>>();
            hub.SetupGet(h => h.Clients).Returns(clients.Object);
            var services = new ServiceCollection();
            services.AddLogicServices(new ConfigurationBuilder().Build());
            services.AddSingleton<ILogger<BadgeRefreshQueue>>(NullLogger<BadgeRefreshQueue>.Instance);
            services.AddScoped<IUnitOfWorkFactory>(_ => CreateFactory());
            services.AddTransient(sp => new BadgeService(sp.GetRequiredService<IUnitOfWorkFactory>(), null!, null!,
                hub.Object, sp.GetRequiredService<BadgeRefreshQueue>()));
            Provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        }

        private IUnitOfWorkFactory CreateFactory()
        {
            var scope = Guid.NewGuid();
            var uow = new Mock<IAppUnitOfWork> { DefaultValue = DefaultValue.Mock };
            uow.Setup(u => u.Dispose()).Callback(() => DisposedUnitsOfWork.Add(scope));
            uow.Setup(u => u.FriendRequestRepository.GetIncomingPendingCount(It.IsAny<Guid>()))
                .Returns((Guid user) => { Calls.Enqueue((user, scope)); return ReadCount(user); });
            uow.Setup(u => u.DirectMessageRepository.GetUnreadCountForUser(It.IsAny<Guid>())).ReturnsAsync(0);
            uow.Setup(u => u.MatchRepository.GetActiveForUserBadge(It.IsAny<Guid>())).ReturnsAsync(new List<MatchBadgeRow>());
            uow.Setup(u => u.MatchChatReadRepository.GetMutedMatchIds(It.IsAny<Guid>())).ReturnsAsync(new List<Guid>());
            uow.Setup(u => u.MatchChatRepository.GetUnreadCountsByMatch(It.IsAny<List<Guid>>(), It.IsAny<Guid>()))
                .ReturnsAsync(new Dictionary<Guid, int>());
            uow.Setup(u => u.TeamJoinRequestRepository.CountPendingForCaptain(It.IsAny<Guid>())).ReturnsAsync(0);
            uow.Setup(u => u.UserHubRepository.GetManagedHubIds(It.IsAny<Guid>())).ReturnsAsync(new List<Guid>());
            uow.Setup(u => u.TournamentRepository.GetHubOwnership(It.IsAny<Guid>())).ReturnsAsync(() => Ownership);
            uow.Setup(u => u.UserHubRepository.GetManagerUserIds(It.IsAny<Guid>())).ReturnsAsync(() => Managers);
            return new ScopedFactory(uow.Object, () => DisposedScopes.Add(scope));
        }

        public async Task<(string Group, BadgeCountsDto Badges)> Next() =>
            await sent.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));

        public async ValueTask DisposeAsync()
        {
            await Queue.StopAsync(CancellationToken.None);
            await Provider.DisposeAsync();
        }
    }

    private sealed class ScopedFactory(IAppUnitOfWork uow, Action onDispose) : IUnitOfWorkFactory, IDisposable
    {
        public IAppUnitOfWork CreateAppUnitOfWork() => uow;
        public void Dispose() => onDispose();
    }
}
