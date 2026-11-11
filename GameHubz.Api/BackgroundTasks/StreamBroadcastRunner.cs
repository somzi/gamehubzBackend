using GameHubz.Data.Context;
using GameHubz.DataModels.Config;
using GameHubz.DataModels.Domain;
using GameHubz.DataModels.Enums;
using GameHubz.Logic.Interfaces;
using GameHubz.Logic.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace GameHubz.Api.BackgroundTasks
{
    /// <summary>
    /// One pass over the live in-app broadcasts (run on its own scope by <see cref="StreamBroadcastTask"/>):
    ///   • heartbeats stopped → asks the platform; ended once the channel is off (or after a hard cap)
    ///   • match over → one "stop your stream" push, then after StopAfterMatchMinutes the next heartbeat
    ///     tells the phone to stop
    ///   • told to stop but still live a few minutes later → ended here
    /// The markers (EndReminderSentOn, StopRequestedOn) are written with ExecuteUpdate, so every step is
    /// one-shot and restart-safe.
    /// </summary>
    public class StreamBroadcastRunner
    {
        private const int MaxStreamsPerSweep = 200;

        // A phone told to stop that is still marked live this long after is ended without it.
        private static readonly TimeSpan StopGrace = TimeSpan.FromMinutes(3);

        private readonly ApplicationContext context;
        private readonly StreamBroadcastService broadcastService;
        private readonly INotificationService notificationService;
        private readonly AnonymousUserContextReader anonymousUserContextReader;
        private readonly StreamBroadcastConfig config;
        private readonly ILogger<StreamBroadcastRunner> logger;

        public StreamBroadcastRunner(
            ApplicationContext context,
            StreamBroadcastService broadcastService,
            INotificationService notificationService,
            AnonymousUserContextReader anonymousUserContextReader,
            IOptions<StreamingConfig> options,
            ILogger<StreamBroadcastRunner> logger)
        {
            this.context = context;
            this.broadcastService = broadcastService;
            this.notificationService = notificationService;
            this.anonymousUserContextReader = anonymousUserContextReader;
            this.config = options.Value.Broadcast;
            this.logger = logger;
        }

        public async Task RunAsync(CancellationToken ct)
        {
            var now = DateTime.UtcNow;
            var heartbeatTimeout = TimeSpan.FromSeconds(Math.Max(30, config.HeartbeatTimeoutSeconds));
            var hardTimeout = TimeSpan.FromMinutes(Math.Max(2, config.HeartbeatHardTimeoutMinutes));
            var stopAfterMatch = TimeSpan.FromMinutes(Math.Max(1, config.StopAfterMatchMinutes));

            var live = await context.Set<MatchStreamEntity>()
                .AsNoTracking()
                .Where(s => s.Source == MatchStreamSource.AppBroadcast && s.Status == MatchStreamStatus.Live)
                .OrderBy(s => s.LastHeartbeatOn)
                .Take(MaxStreamsPerSweep)
                .Select(s => new
                {
                    Stream = s,
                    MatchStatus = s.Match!.Status,
                    s.Match.TournamentId,
                    s.Match.TeamMatchId,
                    TournamentName = s.Match.Tournament!.Name,
                })
                .ToListAsync(ct);

            foreach (var row in live)
            {
                var stream = row.Stream;
                try
                {
                    DateTime lastSign = stream.LastHeartbeatOn ?? stream.StartedAt ?? stream.CreatedOn ?? now;
                    TimeSpan silent = now - lastSign;

                    // The phone went quiet: lost network, app killed, phone off.
                    if (silent > heartbeatTimeout)
                    {
                        if (silent < hardTimeout)
                        {
                            // Our API may have been the one out of reach — only end it once the platform
                            // confirms the channel is off.
                            bool? channelLive = await broadcastService.IsChannelLiveAsync(stream, ct);
                            if (channelLive != false) continue;
                        }

                        await broadcastService.EndAsync(stream, anonymousUserContextReader);
                        continue;
                    }

                    if (stream.StopRequestedOn != null)
                    {
                        if (now - stream.StopRequestedOn.Value > StopGrace)
                            await broadcastService.EndAsync(stream, anonymousUserContextReader);
                        continue;
                    }

                    bool matchOver = row.MatchStatus == MatchStatus.Completed || row.MatchStatus == MatchStatus.NoShow;
                    if (!matchOver) continue;

                    if (stream.EndReminderSentOn == null)
                    {
                        await context.Set<MatchStreamEntity>()
                            .Where(s => s.Id == stream.Id && s.EndReminderSentOn == null)
                            .ExecuteUpdateAsync(s => s.SetProperty(x => x.EndReminderSentOn, now), ct);

                        await this.RemindAsync(stream, row.TournamentId, row.TeamMatchId, row.TournamentName);
                    }
                    else if (now - stream.EndReminderSentOn.Value >= stopAfterMatch)
                    {
                        await context.Set<MatchStreamEntity>()
                            .Where(s => s.Id == stream.Id && s.StopRequestedOn == null)
                            .ExecuteUpdateAsync(s => s.SetProperty(x => x.StopRequestedOn, now), ct);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "Stream broadcast sweep failed for stream {StreamId}.", stream.Id);
                }
            }
        }

        // Push only, no inbox row: the reminder is stale the moment the stream stops.
        private async Task RemindAsync(MatchStreamEntity stream, Guid tournamentId, Guid? teamMatchId, string tournamentName)
        {
            var streamer = await context.Set<UserEntity>()
                .AsNoTracking()
                .Where(u => u.Id == stream.StreamerUserId && u.IsActive)
                .Select(u => new { u.PushToken, u.Language })
                .FirstOrDefaultAsync();

            if (streamer == null || string.IsNullOrWhiteSpace(streamer.PushToken)) return;

            await notificationService.SendLocalizedToOneAsync(
                new PushRecipient(streamer.PushToken, streamer.Language),
                PushText.FromLiteral(tournamentName),
                PushText.FromKey("Push.BroadcastMatchEnded.Body"),
                new { tournamentId, matchId = stream.MatchId, teamMatchId, type = "broadcastMatchEnded" });
        }
    }
}
