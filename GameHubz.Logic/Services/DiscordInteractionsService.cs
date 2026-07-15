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
    /// PING → PONG, plus the /nextmatch, /matches and /profile slash commands. Text handlers reply
    /// within Discord's 3-second window; /profile renders an image card, so it defers first and
    /// edits the original response once the card is ready. Read-only: this service never writes.
    /// </summary>
    public class DiscordInteractionsService : AppBaseService
    {
        private const int InteractionPing = 1;
        private const int InteractionApplicationCommand = 2;
        private const int ResponsePong = 1;
        private const int ResponseChannelMessage = 4;
        private const int FlagEphemeral = 64;
        private const int ResponseDeferredChannelMessage = 5;

        // Discord caps message content at 2000 chars — cap the list well under that so long
        // tournament/hub names can't push a busy player's response over the limit.
        private const int MaxMatchesShown = 12;

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

                // /profile renders an image (QuestPDF + avatar download) that can exceed Discord's
                // 3s window — acknowledge immediately (deferred, ephemeral) and edit the original
                // message with the card once it's ready.
                if (commandName == "profile")
                {
                    string? interactionToken = root.TryGetProperty("token", out var tokenElement)
                        ? tokenElement.GetString()
                        : null;

                    if (!string.IsNullOrEmpty(interactionToken))
                        _ = Task.Run(() => RenderAndSendProfileCardAsync(discordUserId, interactionToken));

                    return new { type = ResponseDeferredChannelMessage, data = new { flags = FlagEphemeral } };
                }

                string content = commandName switch
                {
                    "nextmatch" => await BuildNextMatchAsync(discordUserId),
                    "matches" => await BuildMatchesAsync(discordUserId),
                    _ => "Unknown command.",
                };

                return EphemeralMessage(content);
            }

            return EphemeralMessage("This interaction isn't supported.");
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

        private async Task<string> BuildNextMatchAsync(string? discordUserId)
        {
            var user = await ResolveLinkedUserAsync(discordUserId);
            if (user == null) return NotLinkedMessage;

            // Same projection the mobile home screen uses (GET /api/match/home/{userId}) — active
            // matches for the user with opponent + tournament already resolved.
            var matches = await this.AppUnitOfWork.MatchRepository.GetByUser(user.Id!.Value);

            var next = matches
                .Where(m => m.Status == MatchStatus.Pending || m.Status == MatchStatus.Scheduled)
                .OrderBy(m => m.ScheduledTime ?? DateTime.MaxValue)
                .FirstOrDefault();

            if (next == null)
                return "You have no upcoming matches. 🎉";

            // <t:unix:F> renders in each viewer's local timezone on Discord.
            string when = next.ScheduledTime.HasValue
                ? $"<t:{new DateTimeOffset(DateTime.SpecifyKind(next.ScheduledTime.Value, DateTimeKind.Utc)).ToUnixTimeSeconds()}:F>"
                : "time TBD — agree on a time in the app";

            return $"**Next match:** vs **{next.OpponentNickname ?? next.OpponentName}** — {next.TournamentName} ({next.HubName})\n"
                + $"🕒 {when}\n"
                + $"{shareLinksConfig.BaseUrl}/tournament/{next.TournamentId}";
        }

        private async Task<string> BuildMatchesAsync(string? discordUserId)
        {
            var user = await ResolveLinkedUserAsync(discordUserId);
            if (user == null) return NotLinkedMessage;

            // GetByUser already returns only the user's active (non-completed) matches — the same
            // source /nextmatch uses. Keep the upcoming ones, soonest first; unscheduled ones last.
            var matches = await this.AppUnitOfWork.MatchRepository.GetByUser(user.Id!.Value);

            var active = matches
                .Where(m => m.Status == MatchStatus.Pending || m.Status == MatchStatus.Scheduled)
                .OrderBy(m => m.ScheduledTime ?? DateTime.MaxValue)
                .ToList();

            if (active.Count == 0)
                return "You have no active matches. 🎉";

            var lines = active.Take(MaxMatchesShown).Select(m =>
            {
                // <t:unix:f> renders in each viewer's local timezone; ScheduledTime is stored UTC-kind-
                // unspecified, so pin the kind before converting (same as /nextmatch).
                string when = m.ScheduledTime.HasValue
                    ? $"🕒 <t:{new DateTimeOffset(DateTime.SpecifyKind(m.ScheduledTime.Value, DateTimeKind.Utc)).ToUnixTimeSeconds()}:f>"
                    : "⏳ not scheduled";
                return $"• vs **{m.OpponentNickname ?? m.OpponentName}** — {m.TournamentName} ({m.HubName}) — {when}";
            });

            string body = string.Join("\n", lines);
            string footer = active.Count > MaxMatchesShown
                ? $"\n…and {active.Count - MaxMatchesShown} more — open the app."
                : "";

            return $"**Your active matches ({active.Count}):**\n{body}{footer}";
        }

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
                });

                await EditOriginalWithImageAsync(interactionToken, png);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Discord /profile card failed for {DiscordUserId}.", discordUserId);
                try { await EditOriginalTextAsync(interactionToken, "Couldn't build your profile card right now — try again."); }
                catch { /* best effort */ }
            }
        }

        // Discord interaction follow-up target: the original (deferred) response. No auth header —
        // the interaction token in the URL is the authorization.
        private string OriginalResponseUrl(string interactionToken)
            => $"https://discord.com/api/v10/webhooks/{discordConfig.ApplicationId}/{interactionToken}/messages/@original";

        private async Task EditOriginalWithImageAsync(string interactionToken, byte[] png)
        {
            var client = httpClientFactory.CreateClient();

            using var form = new MultipartFormDataContent();
            string payload = JsonSerializer.Serialize(new
            {
                attachments = new[] { new { id = 0, filename = "profile.png" } }
            });
            form.Add(new StringContent(payload, Encoding.UTF8, "application/json"), "payload_json");

            var imageContent = new ByteArrayContent(png);
            imageContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            form.Add(imageContent, "files[0]", "profile.png");

            using var response = await client.PatchAsync(OriginalResponseUrl(interactionToken), form);
            if (!response.IsSuccessStatusCode)
                logger.LogWarning("Discord /profile followup returned {StatusCode}: {Body}",
                    response.StatusCode, await response.Content.ReadAsStringAsync());
        }

        private async Task EditOriginalTextAsync(string interactionToken, string content)
        {
            var client = httpClientFactory.CreateClient();
            var body = new StringContent(JsonSerializer.Serialize(new { content }), Encoding.UTF8, "application/json");
            using var response = await client.PatchAsync(OriginalResponseUrl(interactionToken), body);
            if (!response.IsSuccessStatusCode)
                logger.LogWarning("Discord /profile text followup returned {StatusCode}.", response.StatusCode);
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

        private async Task<UserEntity?> ResolveLinkedUserAsync(string? discordUserId)
        {
            if (string.IsNullOrWhiteSpace(discordUserId))
                return null;

            return await this.AppUnitOfWork.UserRepository.GetByDiscordUserId(discordUserId);
        }
    }
}
