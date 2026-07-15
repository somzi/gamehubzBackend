using GameHubz.DataModels.Catalog;
using GameHubz.DataModels.Config;
using GameHubz.DataModels.Enums;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace GameHubz.Logic.Services
{
    /// <summary>
    /// Handles Discord interaction payloads (already signature-verified by the controller):
    /// PING → PONG, plus the /nextmatch, /matches and /profile slash commands. All three render
    /// image cards whose generation can exceed Discord's 3-second window, so they defer first and
    /// edit the original response once the card is ready. Read-only: this service never writes.
    /// </summary>
    public class DiscordInteractionsService : AppBaseService
    {
        private const int InteractionPing = 1;
        private const int InteractionApplicationCommand = 2;
        private const int ResponsePong = 1;
        private const int ResponseChannelMessage = 4;
        private const int FlagEphemeral = 64;
        private const int ResponseDeferredChannelMessage = 5;

        private const string NotLinkedMessage = "Link your Discord in the GameHubz app (Profile → Socials).";

        private readonly ShareLinksConfig shareLinksConfig;
        private readonly DiscordConfig discordConfig;
        private readonly IServiceScopeFactory scopeFactory;
        private readonly IHttpClientFactory httpClientFactory;
        private readonly ILogger<DiscordInteractionsService> logger;

        public DiscordInteractionsService(
            IUnitOfWorkFactory unitOfWorkFactory,
            IUserContextReader userContextReader,
            ILocalizationService localizationService,
            IOptions<ShareLinksConfig> shareLinksOptions,
            IOptions<DiscordConfig> discordOptions,
            IServiceScopeFactory scopeFactory,
            IHttpClientFactory httpClientFactory,
            ILogger<DiscordInteractionsService> logger)
            : base(unitOfWorkFactory.CreateAppUnitOfWork(), userContextReader, localizationService)
        {
            this.shareLinksConfig = shareLinksOptions.Value;
            this.discordConfig = discordOptions.Value;
            this.scopeFactory = scopeFactory;
            this.httpClientFactory = httpClientFactory;
            this.logger = logger;
        }

        public async Task<object> HandleAsync(string rawBody)
        {
            using var doc = JsonDocument.Parse(rawBody);
            var root = doc.RootElement;

            int interactionType = root.GetProperty("type").GetInt32();

            if (interactionType == InteractionPing)
                return new { type = ResponsePong };

            if (interactionType == InteractionApplicationCommand)
            {
                string commandName = root.GetProperty("data").GetProperty("name").GetString() ?? "";
                string? discordUserId = ReadInvokerDiscordId(root);

                // Every command renders an image (QuestPDF + avatar downloads) that can exceed
                // Discord's 3s window — acknowledge immediately (deferred, ephemeral) and edit
                // the original message with the card once it's ready.
                string? interactionToken = root.TryGetProperty("token", out var tokenElement)
                    ? tokenElement.GetString()
                    : null;

                Func<Task>? render = commandName switch
                {
                    "profile" => () => RenderAndSendProfileCardAsync(discordUserId, interactionToken!),
                    "matches" => () => RenderAndSendMatchesCardAsync(discordUserId, interactionToken!),
                    "nextmatch" => () => RenderAndSendNextMatchCardAsync(discordUserId, interactionToken!),
                    "lastmatches" => () => RenderAndSendLastMatchesCardAsync(discordUserId, interactionToken!),
                    "vs" => () => RenderAndSendVsCardAsync(discordUserId, ReadUserOption(root, "opponent"), interactionToken!),
                    _ => null,
                };

                if (render == null)
                    return EphemeralMessage("Unknown command.");

                if (!string.IsNullOrEmpty(interactionToken))
                    _ = Task.Run(render);

                return new { type = ResponseDeferredChannelMessage, data = new { flags = FlagEphemeral } };
            }

            return EphemeralMessage("This interaction isn't supported.");
        }

        // Reads a USER-type slash-command option (type 6) — the value is the target's Discord id
        // as a string. `data.resolved.users` also has the full user object, but the id alone is
        // enough to look up the linked GameHubz account.
        private static string? ReadUserOption(JsonElement root, string optionName)
        {
            if (!root.TryGetProperty("data", out var data) ||
                !data.TryGetProperty("options", out var options) ||
                options.ValueKind != JsonValueKind.Array)
                return null;

            foreach (var option in options.EnumerateArray())
            {
                if (option.TryGetProperty("name", out var name) && name.GetString() == optionName
                    && option.TryGetProperty("value", out var value))
                    return value.GetString();
            }

            return null;
        }

        // In a guild the invoker is under member.user, in a bot DM directly under user.
        private static string? ReadInvokerDiscordId(JsonElement root)
        {
            if (root.TryGetProperty("member", out var member)
                && member.TryGetProperty("user", out var memberUser)
                && memberUser.TryGetProperty("id", out var memberUserId))
                return memberUserId.GetString();

            if (root.TryGetProperty("user", out var user)
                && user.TryGetProperty("id", out var userId))
                return userId.GetString();

            return null;
        }

        private static object EphemeralMessage(string content)
            => new { type = ResponseChannelMessage, data = new { content, flags = FlagEphemeral } };

        // /profile is served as a rendered image card. Because generation (DB reads + avatar
        // download + QuestPDF raster) can run past Discord's 3s budget, HandleAsync replies
        // "deferred" and this runs fire-and-forget, then edits the original response. A fresh DI
        // scope is used because the request scope (and this.AppUnitOfWork) is gone by the time
        // this executes.
        private async Task RenderAndSendProfileCardAsync(string? discordUserId, string interactionToken)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(discordUserId))
                {
                    await EditOriginalTextAsync(interactionToken, NotLinkedMessage);
                    return;
                }

                using var scope = scopeFactory.CreateScope();
                var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWorkFactory>().CreateAppUnitOfWork();

                var user = await uow.UserRepository.GetByDiscordUserId(discordUserId);
                if (user == null)
                {
                    await EditOriginalTextAsync(interactionToken, NotLinkedMessage);
                    return;
                }

                var stats = await uow.MatchRepository.GetStatsByUserId(user.Id!.Value);
                int trophies = await uow.TournamentRepository.GetNumberOfTournamentsWonByUserId(user.Id!.Value);
                var performance = await uow.MatchRepository.GetPerformanceByUserIdV2(user.Id!.Value);
                byte[]? avatar = await TryDownloadAsync(user.AvatarUrl);

                byte[] png = DiscordProfileCard.Render(new ProfileCardData
                {
                    Name = string.IsNullOrWhiteSpace(user.Nickname) ? user.Username : user.Nickname!,
                    Ign = user.Username,
                    Region = RegionLabel(user.Region),
                    Country = CountryCatalog.Get(user.Country)?.Name ?? "—",
                    Trophies = trophies,
                    Matches = stats.TotalMatches,
                    Wins = stats.Wins,
                    Losses = stats.Losses,
                    Draws = stats.Draws,
                    WinRate = (int)Math.Round(stats.WinRate),
                    Avatar = avatar,
                    // V2 returns latest-first (the mobile Recent Form source) — the card wants
                    // oldest → latest, left to right.
                    RecentForm = performance.Select(p => p.Outcome).Reverse().ToList(),
                    GeneratedAtUtc = DateTime.UtcNow,
                });

                await EditOriginalWithImageAsync(interactionToken, png, "profile.png");
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Discord /profile card failed for {DiscordUserId}.", discordUserId);
                try { await EditOriginalTextAsync(interactionToken, "Couldn't build your profile card right now — try again."); }
                catch { /* best effort */ }
            }
        }

        // /matches is served as a rendered image card — same deferred flow as /profile: fresh DI
        // scope, render, then edit the original response.
        private async Task RenderAndSendMatchesCardAsync(string? discordUserId, string interactionToken)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(discordUserId))
                {
                    await EditOriginalTextAsync(interactionToken, NotLinkedMessage);
                    return;
                }

                using var scope = scopeFactory.CreateScope();
                var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWorkFactory>().CreateAppUnitOfWork();

                var user = await uow.UserRepository.GetByDiscordUserId(discordUserId);
                if (user == null)
                {
                    await EditOriginalTextAsync(interactionToken, NotLinkedMessage);
                    return;
                }

                // Same projection the mobile home screen uses — active matches with opponent,
                // tournament and round deadline already resolved. Soonest first, unscheduled last.
                var matches = await uow.MatchRepository.GetByUser(user.Id!.Value);

                var active = matches
                    .Where(m => m.Status == MatchStatus.Pending || m.Status == MatchStatus.Scheduled)
                    .OrderBy(m => m.ScheduledTime ?? DateTime.MaxValue)
                    .ToList();

                // Avatars are only fetched for rows that make it onto the card; the same opponent
                // across multiple rounds is downloaded once.
                var avatarCache = new Dictionary<string, byte[]?>();
                var rows = new List<MatchesCardRow>();

                foreach (var match in active.Take(DiscordMatchesCard.MaxRows))
                {
                    byte[]? avatar = null;
                    if (!string.IsNullOrWhiteSpace(match.OpponentAvatarUrl)
                        && !avatarCache.TryGetValue(match.OpponentAvatarUrl, out avatar))
                    {
                        avatar = await TryDownloadAsync(match.OpponentAvatarUrl);
                        avatarCache[match.OpponentAvatarUrl] = avatar;
                    }

                    rows.Add(new MatchesCardRow
                    {
                        Opponent = match.OpponentNickname ?? match.OpponentName,
                        Tournament = match.TournamentName,
                        Hub = match.HubName,
                        ScheduledTime = match.ScheduledTime,
                        Deadline = match.RoundDeadline,
                        Avatar = avatar,
                    });
                }

                byte[] png = DiscordMatchesCard.Render(new MatchesCardData
                {
                    Name = string.IsNullOrWhiteSpace(user.Nickname) ? user.Username : user.Nickname!,
                    Avatar = await TryDownloadAsync(user.AvatarUrl),
                    TotalActive = active.Count,
                    Rows = rows,
                    GeneratedAtUtc = DateTime.UtcNow,
                });

                await EditOriginalWithImageAsync(interactionToken, png, "matches.png");
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Discord /matches card failed for {DiscordUserId}.", discordUserId);
                try { await EditOriginalTextAsync(interactionToken, "Couldn't build your matches card right now — try again."); }
                catch { /* best effort */ }
            }
        }

        // /nextmatch is served as a rendered face-off card — same deferred flow as /profile. The
        // tournament share link rides along as message content (wrapped in <> so Discord doesn't
        // unfurl a preview embed under the card).
        private async Task RenderAndSendNextMatchCardAsync(string? discordUserId, string interactionToken)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(discordUserId))
                {
                    await EditOriginalTextAsync(interactionToken, NotLinkedMessage);
                    return;
                }

                using var scope = scopeFactory.CreateScope();
                var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWorkFactory>().CreateAppUnitOfWork();

                var user = await uow.UserRepository.GetByDiscordUserId(discordUserId);
                if (user == null)
                {
                    await EditOriginalTextAsync(interactionToken, NotLinkedMessage);
                    return;
                }

                // Same projection the mobile home screen uses — the soonest active match wins,
                // unscheduled ones last.
                var matches = await uow.MatchRepository.GetByUser(user.Id!.Value);

                var next = matches
                    .Where(m => m.Status == MatchStatus.Pending || m.Status == MatchStatus.Scheduled)
                    .OrderBy(m => m.ScheduledTime ?? DateTime.MaxValue)
                    .FirstOrDefault();

                var data = new NextMatchCardData
                {
                    PlayerName = string.IsNullOrWhiteSpace(user.Nickname) ? user.Username : user.Nickname!,
                    PlayerAvatar = await TryDownloadAsync(user.AvatarUrl),
                    HasMatch = next != null,
                    GeneratedAtUtc = DateTime.UtcNow,
                };

                string? content = null;
                if (next != null)
                {
                    data.Opponent = next.OpponentNickname ?? next.OpponentName;
                    data.OpponentAvatar = await TryDownloadAsync(next.OpponentAvatarUrl);
                    data.Tournament = next.TournamentName;
                    data.Hub = next.HubName;
                    data.ScheduledTime = next.ScheduledTime;
                    data.Deadline = next.RoundDeadline;
                    content = $"<{shareLinksConfig.BaseUrl}/tournament/{next.TournamentId}>";
                }

                byte[] png = DiscordNextMatchCard.Render(data);

                await EditOriginalWithImageAsync(interactionToken, png, "nextmatch.png", content);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Discord /nextmatch card failed for {DiscordUserId}.", discordUserId);
                try { await EditOriginalTextAsync(interactionToken, "Couldn't build your next-match card right now — try again."); }
                catch { /* best effort */ }
            }
        }

        // /lastmatches is the completed-history mirror of /matches — same deferred flow, but the
        // rows come from GetLastMatchesByUserId (all-time results with score + IsWin already
        // resolved). Drives the "Profile / Upcoming / History" trio on Discord.
        private async Task RenderAndSendLastMatchesCardAsync(string? discordUserId, string interactionToken)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(discordUserId))
                {
                    await EditOriginalTextAsync(interactionToken, NotLinkedMessage);
                    return;
                }

                using var scope = scopeFactory.CreateScope();
                var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWorkFactory>().CreateAppUnitOfWork();

                var user = await uow.UserRepository.GetByDiscordUserId(discordUserId);
                if (user == null)
                {
                    await EditOriginalTextAsync(interactionToken, NotLinkedMessage);
                    return;
                }

                var stats = await uow.MatchRepository.GetStatsByUserId(user.Id!.Value);
                var last = await uow.MatchRepository.GetLastMatchesByUserId(user.Id!.Value, DiscordLastMatchesCard.MaxRows, 0);

                var avatarCache = new Dictionary<string, byte[]?>();
                var rows = new List<LastMatchesCardRow>();

                foreach (var m in last)
                {
                    byte[]? avatar = null;
                    if (!string.IsNullOrWhiteSpace(m.OpponentAvatarUrl)
                        && !avatarCache.TryGetValue(m.OpponentAvatarUrl, out avatar))
                    {
                        avatar = await TryDownloadAsync(m.OpponentAvatarUrl);
                        avatarCache[m.OpponentAvatarUrl] = avatar;
                    }

                    // IsWin is nullable (null = draw when scores are recorded but no winner set);
                    // GetStatsByUserId only counts non-null wins/losses, so draws show up as the
                    // difference between total and W+L.
                    string outcome = m.IsWin == true ? "W" : m.IsWin == false ? "L" : "D";

                    rows.Add(new LastMatchesCardRow
                    {
                        Opponent = m.OpponentName,
                        Tournament = m.TournamentName,
                        Hub = m.HubName,
                        Outcome = outcome,
                        MyScore = m.UserScore,
                        OpponentScore = m.OpponentScore,
                        PlayedAt = m.ScheduledTime,
                        Avatar = avatar,
                    });
                }

                byte[] png = DiscordLastMatchesCard.Render(new LastMatchesCardData
                {
                    Name = string.IsNullOrWhiteSpace(user.Nickname) ? user.Username : user.Nickname!,
                    Wins = stats.Wins,
                    Losses = stats.Losses,
                    Draws = stats.Draws,
                    Rows = rows,
                    GeneratedAtUtc = DateTime.UtcNow,
                });

                await EditOriginalWithImageAsync(interactionToken, png, "lastmatches.png");
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Discord /lastmatches card failed for {DiscordUserId}.", discordUserId);
                try { await EditOriginalTextAsync(interactionToken, "Couldn't build your last-matches card right now — try again."); }
                catch { /* best effort */ }
            }
        }

        // /vs @opponent renders a head-to-head card. Both sides must be linked to a GameHubz
        // account — the invoker via their Discord id, the opponent via the USER option — otherwise
        // the followup is a text hint telling the invoker (or the missing side) what to do. Self-
        // targeting short-circuits with a friendly message instead of a zero-row H2H.
        private async Task RenderAndSendVsCardAsync(string? invokerDiscordId, string? opponentDiscordId, string interactionToken)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(invokerDiscordId))
                {
                    await EditOriginalTextAsync(interactionToken, NotLinkedMessage);
                    return;
                }

                if (string.IsNullOrWhiteSpace(opponentDiscordId))
                {
                    await EditOriginalTextAsync(interactionToken, "Pick an opponent — usage: /vs @player.");
                    return;
                }

                if (invokerDiscordId == opponentDiscordId)
                {
                    await EditOriginalTextAsync(interactionToken, "You can't face yourself — pick another player.");
                    return;
                }

                using var scope = scopeFactory.CreateScope();
                var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWorkFactory>().CreateAppUnitOfWork();

                var me = await uow.UserRepository.GetByDiscordUserId(invokerDiscordId);
                if (me == null)
                {
                    await EditOriginalTextAsync(interactionToken, NotLinkedMessage);
                    return;
                }

                var opponent = await uow.UserRepository.GetByDiscordUserId(opponentDiscordId);
                if (opponent == null)
                {
                    await EditOriginalTextAsync(interactionToken, "That player hasn't linked a GameHubz account yet.");
                    return;
                }

                var h2h = await uow.MatchRepository.GetHeadToHead(me.Id!.Value, opponent.Id!.Value);

                byte[] png = DiscordVsCard.Render(new VsCardData
                {
                    PlayerName = string.IsNullOrWhiteSpace(me.Nickname) ? me.Username : me.Nickname!,
                    PlayerAvatar = await TryDownloadAsync(me.AvatarUrl),
                    OpponentName = string.IsNullOrWhiteSpace(opponent.Nickname) ? opponent.Username : opponent.Nickname!,
                    OpponentAvatar = await TryDownloadAsync(opponent.AvatarUrl),
                    TotalMatches = h2h.TotalMatches,
                    MyWins = h2h.MyWins,
                    OpponentWins = h2h.OpponentWins,
                    Draws = h2h.Draws,
                    LastOutcome = h2h.LastOutcome,
                    LastMyScore = h2h.LastMyScore,
                    LastOpponentScore = h2h.LastOpponentScore,
                    LastMatchTime = h2h.LastMatchTime,
                    LastTournamentName = h2h.LastTournamentName,
                    LastHubName = h2h.LastHubName,
                    GeneratedAtUtc = DateTime.UtcNow,
                });

                await EditOriginalWithImageAsync(interactionToken, png, "vs.png");
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Discord /vs card failed for {DiscordUserId} vs {OpponentDiscordId}.",
                    invokerDiscordId, opponentDiscordId);
                try { await EditOriginalTextAsync(interactionToken, "Couldn't build the head-to-head card right now — try again."); }
                catch { /* best effort */ }
            }
        }

        // Discord interaction follow-up target: the original (deferred) response. No auth header —
        // the interaction token in the URL is the authorization.
        private string OriginalResponseUrl(string interactionToken)
            => $"https://discord.com/api/v10/webhooks/{discordConfig.ApplicationId}/{interactionToken}/messages/@original";

        private async Task EditOriginalWithImageAsync(string interactionToken, byte[] png, string filename, string? content = null)
        {
            var client = httpClientFactory.CreateClient();

            using var form = new MultipartFormDataContent();
            var attachments = new[] { new { id = 0, filename } };
            string payload = content == null
                ? JsonSerializer.Serialize(new { attachments })
                : JsonSerializer.Serialize(new { content, attachments });
            form.Add(new StringContent(payload, Encoding.UTF8, "application/json"), "payload_json");

            var imageContent = new ByteArrayContent(png);
            imageContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            form.Add(imageContent, "files[0]", filename);

            using var response = await client.PatchAsync(OriginalResponseUrl(interactionToken), form);
            if (!response.IsSuccessStatusCode)
                logger.LogWarning("Discord card followup returned {StatusCode}: {Body}",
                    response.StatusCode, await response.Content.ReadAsStringAsync());
        }

        private async Task EditOriginalTextAsync(string interactionToken, string content)
        {
            var client = httpClientFactory.CreateClient();
            var body = new StringContent(JsonSerializer.Serialize(new { content }), Encoding.UTF8, "application/json");
            using var response = await client.PatchAsync(OriginalResponseUrl(interactionToken), body);
            if (!response.IsSuccessStatusCode)
                logger.LogWarning("Discord text followup returned {StatusCode}.", response.StatusCode);
        }

        private async Task<byte[]?> TryDownloadAsync(string? url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return null;

            try
            {
                var client = httpClientFactory.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(8);
                return await client.GetByteArrayAsync(url);
            }
            catch
            {
                return null;
            }
        }

        private static string RegionLabel(RegionType region) => region switch
        {
            RegionType.NA => "NA",
            RegionType.EUROPE => "Europe",
            RegionType.ASIA => "Asia",
            RegionType.SA => "SA",
            RegionType.AFRICA => "Africa",
            RegionType.OCEANIA => "Oceania",
            _ => "Global",
        };
    }
}
