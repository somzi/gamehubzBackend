using GameHubz.DataModels.Config;
using GameHubz.DataModels.Enums;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace GameHubz.Logic.Services
{
    /// <summary>
    /// Handles Discord interaction payloads (already signature-verified by the controller):
    /// PING → PONG, plus the /nextmatch and /profile slash commands. Responses are plain-text,
    /// ephemeral (flags 64), and must go out within Discord's 3-second window — every handler is
    /// a couple of indexed reads. Read-only: this service never writes.
    /// </summary>
    public class DiscordInteractionsService : AppBaseService
    {
        private const int InteractionPing = 1;
        private const int InteractionApplicationCommand = 2;
        private const int ResponsePong = 1;
        private const int ResponseChannelMessage = 4;
        private const int FlagEphemeral = 64;

        // Discord caps message content at 2000 chars — cap the list well under that so long
        // tournament/hub names can't push a busy player's response over the limit.
        private const int MaxMatchesShown = 12;

        private const string NotLinkedMessage = "Link your Discord in the GameHubz app (Profile → Socials).";

        private readonly ShareLinksConfig shareLinksConfig;

        public DiscordInteractionsService(
            IUnitOfWorkFactory unitOfWorkFactory,
            IUserContextReader userContextReader,
            ILocalizationService localizationService,
            IOptions<ShareLinksConfig> shareLinksOptions)
            : base(unitOfWorkFactory.CreateAppUnitOfWork(), userContextReader, localizationService)
        {
            this.shareLinksConfig = shareLinksOptions.Value;
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

                string content = commandName switch
                {
                    "nextmatch" => await BuildNextMatchAsync(discordUserId),
                    "matches" => await BuildMatchesAsync(discordUserId),
                    "profile" => await BuildProfileAsync(discordUserId),
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

        private async Task<string> BuildProfileAsync(string? discordUserId)
        {
            var user = await ResolveLinkedUserAsync(discordUserId);
            if (user == null) return NotLinkedMessage;

            // Same sources as the mobile profile stats (GET /api/UserProfile/v2/{id}/stats).
            var stats = await this.AppUnitOfWork.MatchRepository.GetStatsByUserId(user.Id!.Value);
            var tournamentsWon = await this.AppUnitOfWork.TournamentRepository.GetNumberOfTournamentsWonByUserId(user.Id!.Value);

            int winRate = stats.TotalMatches > 0
                ? (int)Math.Round(stats.Wins * 100.0 / stats.TotalMatches)
                : 0;

            return $"**{user.Nickname ?? user.Username}** (@{user.Username})\n"
                + $"🎮 {stats.TotalMatches} matches • {stats.Wins}W / {stats.Losses}L • {winRate}% win rate • 🏆 {tournamentsWon} tournaments won\n"
                + $"{shareLinksConfig.BaseUrl}/user/{user.Id}";
        }

        private async Task<UserEntity?> ResolveLinkedUserAsync(string? discordUserId)
        {
            if (string.IsNullOrWhiteSpace(discordUserId))
                return null;

            return await this.AppUnitOfWork.UserRepository.GetByDiscordUserId(discordUserId);
        }
    }
}
