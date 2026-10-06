using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GameHubz.Logic.Services;

/// <summary>
/// Best-effort badge refreshes after committed mutations. Only IDs cross the request boundary.
/// Four serial partitions bound database concurrency and keep a user's sends ordered in this
/// process. Repeated queued requests collapse into one; a change during a running refresh
/// queues one more pass so the final state is never lost.
/// </summary>
public sealed class BadgeRefreshQueue(IServiceScopeFactory scopeFactory, ILogger<BadgeRefreshQueue> logger)
    : BackgroundService
{
    private readonly Partition[] partitions = Enumerable.Range(0, 4).Select(_ => new Partition()).ToArray();

    public void EnqueueUser(Guid userId) => Enqueue(new Target(userId, false));
    public void EnqueueTournamentManagers(Guid tournamentId) => Enqueue(new Target(tournamentId, true));

    private void Enqueue(Target target)
    {
        var partition = partitions[(uint)target.Id.GetHashCode() % (uint)partitions.Length];
        lock (partition.Pending)
        {
            if (partition.Pending.Add(target) && !partition.Channel.Writer.TryWrite(target))
                partition.Pending.Remove(target);
        }
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.WhenAll(partitions.Select(partition => RunPartitionAsync(partition, stoppingToken)));

    private async Task RunPartitionAsync(Partition partition, CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var target in partition.Channel.Reader.ReadAllAsync(stoppingToken))
            {
                stoppingToken.ThrowIfCancellationRequested();
                lock (partition.Pending) partition.Pending.Remove(target);
                try
                {
                    await using var scope = scopeFactory.CreateAsyncScope();
                    var badges = scope.ServiceProvider.GetRequiredService<BadgeService>();
                    // UnitOfWorkFactory creates its DbContext itself; disposing only the DI
                    // scope does not own that context. This job is its only consumer.
                    using var unitOfWork = badges.AppUnitOfWork;
                    if (target.TournamentManagers)
                        await badges.QueueTournamentManagersAsync(target.Id);
                    else
                        await badges.ComputeAndPushAsync(target.Id);
                }
                catch (Exception ex)
                {
                    // A failed refresh must not stop subsequent users or subsequent changes.
                    logger.LogWarning(ex, "Badge refresh failed for {TargetId} (managers: {Managers})",
                        target.Id, target.TournamentManagers);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        foreach (var partition in partitions) partition.Channel.Writer.TryComplete();
        await base.StopAsync(cancellationToken);
    }

    private readonly record struct Target(Guid Id, bool TournamentManagers);

    private sealed class Partition
    {
        public Channel<Target> Channel { get; } = System.Threading.Channels.Channel.CreateUnbounded<Target>(
            new UnboundedChannelOptions { SingleReader = true, AllowSynchronousContinuations = false });
        public HashSet<Target> Pending { get; } = new();
    }
}
