using GameHubz.DataModels.Config;
using GameHubz.DataModels.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GameHubz.Logic.Services
{
    /// <summary>
    /// Broadcast from the GameHubz app: the phone captures its own screen and sends it to the player's
    /// linked Twitch / Kick channel. The server never touches the video — it hands out the stream key,
    /// keeps the MatchStream row viewers see in step with what the phone reports, and resolves the VOD.
    ///
    /// Flow: prepare (JWT) → the phone connects → started → heartbeat every ~30 s → stopped. The phone's
    /// screen-capture process (an iOS broadcast extension / an Android service) has no GameHubz login, so
    /// after prepare it authenticates with the session token alone. The row only becomes Live once the
    /// phone is actually sending, so a cancelled system prompt never shows viewers a dead stream.
    /// </summary>
    public class StreamBroadcastService : AppBaseService
    {
        private const string PendingCachePrefix = "stream:broadcast:pending:";

        // Twitch caps titles at 140 characters; Kick's limit is looser.
        private const int MaxTitleLength = 140;

        private readonly StreamAccountService streamAccountService;
        private readonly StreamVodResolver streamVodResolver;
        private readonly ICacheService cacheService;
        private readonly AnonymousUserContextReader anonymousUserContextReader;
        private readonly StreamBroadcastConfig config;
        private readonly ILogger<StreamBroadcastService> logger;

        public StreamBroadcastService(
            IUnitOfWorkFactory unitOfWorkFactory,
            IUserContextReader userContextReader,
            ILocalizationService localizationService,
            StreamAccountService streamAccountService,
            StreamVodResolver streamVodResolver,
            ICacheService cacheService,
            AnonymousUserContextReader anonymousUserContextReader,
            IOptions<StreamingConfig> options,
            ILogger<StreamBroadcastService> logger)
            : base(unitOfWorkFactory.CreateAppUnitOfWork(), userContextReader, localizationService)
        {
            this.streamAccountService = streamAccountService;
            this.streamVodResolver = streamVodResolver;
            this.cacheService = cacheService;
            this.anonymousUserContextReader = anonymousUserContextReader;
            this.config = options.Value.Broadcast;
            this.logger = logger;
        }

        /// <summary>
        /// Everything the phone needs to go live on this match: the ingest url + key of the caller's linked
        /// channel, a session token for its heartbeats and the overlay text. Sets the stream title on the
        /// platform. Nothing is shown to viewers until the phone reports it is sending.
        /// </summary>
        public async Task<BroadcastSessionDto> Prepare(Guid matchId, PrepareBroadcastRequest request)
        {
            var user = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();

            if (request.Platform != SocialType.Twitch && request.Platform != SocialType.Kick)
                throw new BusinessRuleException(this.LocalizationService["BusinessRule.UnsupportedStreamPlatform"]);

            var match = await this.AppUnitOfWork.MatchRepository.GetWithParticipants(matchId)
                ?? throw new BusinessRuleException(this.LocalizationService["BusinessRule.MatchNotFound"]);

            if (!IsMatchParticipant(match, user.UserId))
                throw new BusinessRuleException(this.LocalizationService["BusinessRule.OnlyParticipantsStream"]);

            if (match.Status == MatchStatus.Completed || match.Status == MatchStatus.NoShow)
                throw new BusinessRuleException(this.LocalizationService["BusinessRule.BroadcastMatchFinished"]);

            var account = await this.AppUnitOfWork.UserStreamAccountRepository.GetByUserAndPlatform(user.UserId, request.Platform)
                ?? throw new BusinessRuleException(this.LocalizationService["BusinessRule.StreamAccountNotLinked"]);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            var provider = this.streamAccountService.ProviderOrThrow(request.Platform);
            var (ingest, accessToken) = await this.GetIngestAsync(provider, account, cts.Token);

            var overlay = await this.BuildOverlayAsync(match, user.UserId);

            // Best effort: a channel whose title can't be set still streams fine.
            try
            {
                await provider.TrySetTitleAsync(accessToken, account.ExternalUserId, BuildTitle(overlay), cts.Token);
            }
            catch (Exception ex) when (ex is StreamPlatformUnavailableException or StreamTokenRejectedException or TaskCanceledException)
            {
                this.logger.LogInformation(ex, "Setting the stream title failed for match {MatchId}.", matchId);
            }

            string sessionToken = StreamTokenProtector.NewToken();
            await this.cacheService.SetAsync(PendingCachePrefix + StreamTokenProtector.Hash(sessionToken), new PendingBroadcast
            {
                UserId = user.UserId,
                MatchId = matchId,
                Platform = (int)request.Platform,
                Login = account.Login,
            }, TimeSpan.FromMinutes(Math.Max(1, this.config.PendingSessionMinutes)));

            return new BroadcastSessionDto
            {
                SessionToken = sessionToken,
                Platform = request.Platform,
                ChannelHandle = account.Login,
                IngestUrl = ingest.ServerUrl,
                StreamKey = ingest.StreamKey,
                HeartbeatIntervalSeconds = Math.Max(10, this.config.HeartbeatIntervalSeconds),
                Overlay = overlay,
            };
        }

        /// <summary>
        /// The phone connected to the ingest server: the stream becomes Live for viewers. Idempotent — a
        /// retried call returns the row the first one made.
        /// </summary>
        public async Task<BroadcastStartedDto> Started(string? sessionToken)
        {
            string hash = HashOrThrow(sessionToken);

            var existing = await this.AppUnitOfWork.MatchStreamRepository.GetByBroadcastTokenHash(hash);
            if (existing != null) return new BroadcastStartedDto { StreamId = existing.Id!.Value };

            var pending = await this.cacheService.GetAsync<PendingBroadcast>(PendingCachePrefix + hash)
                ?? throw new BusinessRuleException(this.LocalizationService["BusinessRule.BroadcastSessionExpired"]);

            var stream = await this.GoLiveAsync(hash, pending)
                ?? throw new BusinessRuleException(this.LocalizationService["BusinessRule.BroadcastSessionExpired"]);
            return new BroadcastStartedDto { StreamId = stream.Id!.Value };
        }

        /// <summary>
        /// Puts the prepared session on air: the row viewers see. Called by "started", or by the first
        /// heartbeat when that report was lost on the way — both may race each other and the stop.
        ///
        /// The insert is the decision: it only lands while no row holds the session token. Null when it
        /// lost to a stop (that left a hidden row on the token) — the session must not go live.
        /// </summary>
        private async Task<MatchStreamEntity?> GoLiveAsync(string hash, PendingBroadcast pending)
        {
            var now = DateTime.UtcNow;
            var id = Guid.NewGuid();

            bool inserted = await this.AppUnitOfWork.MatchStreamRepository.TryInsertLiveBroadcast(
                id, pending.MatchId, pending.UserId, (SocialType)pending.Platform, pending.Login, hash, now);

            // Only after the row exists (or lost): a stop that finds no pending session re-reads the row.
            await this.cacheService.RemoveAsync(PendingCachePrefix + hash);

            if (inserted)
            {
                // One live card per streamer per match: only the newest live row stays (a hand-attached
                // stream, an earlier session that never reported its end — or this one, if a newer session
                // landed first). Its phone hears "stop" on the next heartbeat.
                await this.AppUnitOfWork.MatchStreamRepository.KeepNewestLiveRow(pending.MatchId, pending.UserId, now);
            }

            // A parallel "started" / heartbeat that inserted it, or null: the stop got there first.
            return await this.AppUnitOfWork.MatchStreamRepository.GetByBroadcastTokenHash(hash);
        }

        /// <summary>
        /// The phone is still sending. Answers whether it should stop: the row was ended here (the
        /// streamer or an admin stopped it, the sweep timed it out) or the match is long over.
        /// </summary>
        public async Task<BroadcastHeartbeatDto> Heartbeat(string? sessionToken)
        {
            string hash = HashOrThrow(sessionToken);

            var stream = await this.AppUnitOfWork.MatchStreamRepository.GetByBroadcastTokenHash(hash);
            if (stream == null)
            {
                // The phone only beats once it is sending, so a prepared session without a row means its
                // "started" report never arrived: put it on air now. Anything else is a session we don't know.
                var pending = await this.cacheService.GetAsync<PendingBroadcast>(PendingCachePrefix + hash);
                if (pending == null) return new BroadcastHeartbeatDto { Stop = true };

                var live = await this.GoLiveAsync(hash, pending);
                return new BroadcastHeartbeatDto { Stop = live == null || live.Status != MatchStreamStatus.Live };
            }

            if (stream.Status != MatchStreamStatus.Live || stream.StopRequestedOn != null)
                return new BroadcastHeartbeatDto { Stop = true };

            bool stillLive = await this.AppUnitOfWork.MatchStreamRepository.TouchHeartbeat(stream.Id!.Value, DateTime.UtcNow);
            return new BroadcastHeartbeatDto { Stop = !stillLive };
        }

        /// <summary>
        /// The phone stopped sending (the streamer's Stop, or it was told to). Races a late "started" /
        /// heartbeat safely: a session that never went live is claimed with a hidden row on its token, so
        /// it can't go live afterwards; if the session got on air first, its row is ended instead.
        /// </summary>
        public async Task Stopped(string? sessionToken)
        {
            string hash = HashOrThrow(sessionToken);

            var stream = await this.AppUnitOfWork.MatchStreamRepository.GetByBroadcastTokenHash(hash);
            if (stream == null)
            {
                var pending = await this.cacheService.GetAsync<PendingBroadcast>(PendingCachePrefix + hash);
                if (pending != null)
                {
                    bool claimed = await this.AppUnitOfWork.MatchStreamRepository.TryClaimStoppedBroadcast(
                        Guid.NewGuid(), pending.MatchId, pending.UserId, (SocialType)pending.Platform, pending.Login, hash, DateTime.UtcNow);
                    await this.cacheService.RemoveAsync(PendingCachePrefix + hash);
                    if (claimed) return;
                }

                // Gone live meanwhile: activation inserts the row before it clears the pending session,
                // so if the pending session is gone (or the claim lost), the row is there to end.
                stream = await this.AppUnitOfWork.MatchStreamRepository.GetByBroadcastTokenHash(hash);
                if (stream == null) return;
            }

            await this.EndAsync(stream, this.anonymousUserContextReader);
        }

        /// <summary>
        /// "Stop stream" from the app itself. The phone stops its own sending; this closes the row right
        /// away rather than waiting for the capture process to report, and is the fallback when it can't.
        /// </summary>
        public async Task StopFromApp(Guid matchId)
        {
            var user = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();

            var stream = await this.AppUnitOfWork.MatchStreamRepository.GetLatestByMatchAndStreamer(matchId, user.UserId);
            if (stream == null || stream.Source != MatchStreamSource.AppBroadcast) return;

            await this.EndAsync(stream, this.UserContextReader);
        }

        /// <summary>
        /// Ends a broadcast row and resolves its VOD from the platform. Shared with the sweep. The row is
        /// left to the caller's tracking state as loaded (repositories read untracked).
        /// </summary>
        public async Task EndAsync(MatchStreamEntity stream, IUserContextReader auditReader)
        {
            if (stream.Status != MatchStreamStatus.Live) return;

            stream.Status = MatchStreamStatus.Ended;
            stream.EndedAt ??= DateTime.UtcNow;

            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                var startedAt = stream.StartedAt ?? stream.CreatedOn ?? stream.EndedAt.Value;
                string? vod = await this.streamVodResolver.ResolveVodUrlAsync(
                    stream.Platform, stream.ChannelHandle, startedAt, stream.EndedAt.Value, cts.Token);
                if (!string.IsNullOrWhiteSpace(vod)) stream.VodUrl = vod;
            }
            catch (Exception ex)
            {
                // The VOD can still come later (Kick re-resolves on read) or be pasted by hand.
                this.logger.LogInformation(ex, "VOD resolution failed when ending broadcast {StreamId}.", stream.Id);
            }

            await this.AppUnitOfWork.MatchStreamRepository.UpdateEntity(stream, auditReader);
            await this.SaveAsync();
        }

        /// <summary>
        /// Asks the platform whether the channel is still live — used by the sweep before it times out a
        /// broadcast whose heartbeats stopped (the API might have been the one that was unreachable).
        /// Null when the platform could not be asked.
        /// </summary>
        public async Task<bool?> IsChannelLiveAsync(MatchStreamEntity stream, CancellationToken ct)
        {
            var account = await this.AppUnitOfWork.UserStreamAccountRepository.GetByUserAndPlatform(stream.StreamerUserId, stream.Platform);
            if (account == null) return null;

            try
            {
                string accessToken = await this.streamAccountService.GetAccessTokenAsync(account, ct);
                return await this.streamAccountService.ProviderOrThrow(stream.Platform)
                    .IsLiveAsync(accessToken, account.ExternalUserId, ct);
            }
            catch (Exception ex) when (ex is BusinessRuleException or StreamPlatformUnavailableException
                or StreamTokenRejectedException or TaskCanceledException)
            {
                return null;
            }
        }

        /// <summary>
        /// The channel's ingest url + key, read as its owner. A token the platform refuses before its
        /// expiry (revoked, invalidated) gets one forced refresh and one retry; refused again, the link is
        /// dead and the account is marked for re-link. A platform that is down is a "try again".
        /// </summary>
        private async Task<(StreamIngest Ingest, string AccessToken)> GetIngestAsync(
            IStreamAccountProvider provider, UserStreamAccountEntity account, CancellationToken ct)
        {
            string accessToken = await this.streamAccountService.GetAccessTokenAsync(account, ct);

            try
            {
                StreamIngest? ingest;
                try
                {
                    ingest = await provider.GetIngestAsync(accessToken, account.ExternalUserId, ct);
                }
                catch (StreamTokenRejectedException)
                {
                    accessToken = await this.streamAccountService.GetAccessTokenAsync(account, ct, forceRefresh: true);
                    try
                    {
                        ingest = await provider.GetIngestAsync(accessToken, account.ExternalUserId, ct);
                    }
                    catch (StreamTokenRejectedException)
                    {
                        await this.streamAccountService.MarkNeedsRelinkAsync(account);
                        throw new BusinessRuleException(this.LocalizationService["BusinessRule.StreamAccountNeedsRelink"]);
                    }
                }

                return ingest == null
                    ? throw new BusinessRuleException(this.LocalizationService["BusinessRule.BroadcastIngestUnavailable"])
                    : (ingest, accessToken);
            }
            catch (Exception ex) when (ex is StreamPlatformUnavailableException or TaskCanceledException)
            {
                throw new BusinessRuleException(this.LocalizationService["BusinessRule.BroadcastIngestUnavailable"]);
            }
        }

        private string HashOrThrow(string? sessionToken)
        {
            if (string.IsNullOrWhiteSpace(sessionToken))
                throw new BusinessRuleException(this.LocalizationService["BusinessRule.BroadcastSessionExpired"]);
            return StreamTokenProtector.Hash(sessionToken.Trim());
        }

        private async Task<BroadcastOverlayDto> BuildOverlayAsync(MatchEntity match, Guid streamerUserId)
        {
            var tournament = await this.AppUnitOfWork.TournamentRepository.ShallowGetById(match.TournamentId);

            // Team sub-matches carry the two players on the match; solo and team ties use the participants.
            string home = await this.SideNameAsync(match.HomeUserId, match.HomeParticipant);
            string away = await this.SideNameAsync(match.AwayUserId, match.AwayParticipant);

            bool streamerIsHome = match.HomeUserId == streamerUserId
                || match.HomeParticipant?.UserId == streamerUserId
                || match.HomeParticipant?.Team?.Members.Any(m => m.UserId == streamerUserId) == true;

            return new BroadcastOverlayDto
            {
                TournamentName = tournament?.Name ?? string.Empty,
                HomeName = home,
                AwayName = away,
                RoundNumber = match.RoundNumber,
                StreamerIsHome = streamerIsHome,
            };
        }

        private async Task<string> SideNameAsync(Guid? playerUserId, TournamentParticipantEntity? participant)
        {
            Guid? userId = playerUserId ?? participant?.UserId;
            if (userId != null)
            {
                var user = await this.AppUnitOfWork.UserRepository.ShallowGetById(userId.Value);
                if (user != null) return string.IsNullOrWhiteSpace(user.Nickname) ? user.Username : user.Nickname!;
            }

            if (!string.IsNullOrWhiteSpace(participant?.Team?.TeamName)) return participant!.Team!.TeamName;

            return "TBD";
        }

        private static string BuildTitle(BroadcastOverlayDto overlay)
        {
            string title = string.IsNullOrWhiteSpace(overlay.TournamentName)
                ? $"{overlay.HomeName} vs {overlay.AwayName} | GameHubz"
                : $"{overlay.HomeName} vs {overlay.AwayName} · {overlay.TournamentName} | GameHubz";
            return title.Length <= MaxTitleLength ? title : title[..(MaxTitleLength - 1)] + "…";
        }

        private static bool IsMatchParticipant(MatchEntity match, Guid userId)
        {
            // Same rule as the hand-attached stream (MatchService.IsMatchParticipant).
            if (match.HomeUserId == userId || match.AwayUserId == userId) return true;

            return (match.HomeParticipant != null &&
                        (match.HomeParticipant.UserId == userId ||
                         match.HomeParticipant.Team?.Members.Any(m => m.UserId == userId) == true)) ||
                   (match.AwayParticipant != null &&
                        (match.AwayParticipant.UserId == userId ||
                         match.AwayParticipant.Team?.Members.Any(m => m.UserId == userId) == true));
        }

        private sealed class PendingBroadcast
        {
            public Guid UserId { get; set; }
            public Guid MatchId { get; set; }
            public int Platform { get; set; }
            public string Login { get; set; } = string.Empty;
        }
    }
}
