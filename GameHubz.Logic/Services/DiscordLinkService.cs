using GameHubz.DataModels.Config;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net.Http.Headers;
using System.Text.Json;

namespace GameHubz.Logic.Services
{
    /// <summary>
    /// Discord account linking, fully server-side (the mobile app only opens the authorize URL in
    /// the system browser — no native OAuth modules). The one-time state entry in Redis is what
    /// binds the anonymous callback to a GameHubz account; the OAuth tokens are used once for
    /// GET /users/@me and then discarded — we persist only DiscordUserId + DiscordUsername.
    /// </summary>
    public class DiscordLinkService : AppBaseService
    {
        private const string StateCachePrefix = "discord:link_state:";
        private static readonly TimeSpan StateTtl = TimeSpan.FromMinutes(10);

        private readonly ICacheService cacheService;
        private readonly IHttpClientFactory httpClientFactory;
        private readonly AnonymousUserContextReader anonymousUserContextReader;
        private readonly DiscordConfig config;
        private readonly ShareLinksConfig shareLinksConfig;
        private readonly ILogger<DiscordLinkService> logger;

        public DiscordLinkService(
            IUnitOfWorkFactory unitOfWorkFactory,
            IUserContextReader userContextReader,
            ILocalizationService localizationService,
            ICacheService cacheService,
            IHttpClientFactory httpClientFactory,
            AnonymousUserContextReader anonymousUserContextReader,
            IOptions<DiscordConfig> discordOptions,
            IOptions<ShareLinksConfig> shareLinksOptions,
            ILogger<DiscordLinkService> logger)
            : base(unitOfWorkFactory.CreateAppUnitOfWork(), userContextReader, localizationService)
        {
            this.cacheService = cacheService;
            this.httpClientFactory = httpClientFactory;
            this.anonymousUserContextReader = anonymousUserContextReader;
            this.config = discordOptions.Value;
            this.shareLinksConfig = shareLinksOptions.Value;
            this.logger = logger;
        }

        /// <summary>Creates the one-time state and returns the Discord authorize URL to open.</summary>
        public async Task<string> StartLinkAsync()
        {
            var caller = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();

            if (string.IsNullOrWhiteSpace(config.ClientId) || string.IsNullOrWhiteSpace(config.RedirectUri))
                throw new BusinessRuleException("Discord linking isn't configured on this server yet.");

            // One-time, TTL-bound state tied to the caller. The callback is anonymous, so this
            // entry is the only thing binding the returning code to a GameHubz account — without
            // it the flow would be open to CSRF / session fixation.
            string state = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
            await cacheService.SetAsync(StateCachePrefix + state, caller.UserId.ToString(), StateTtl);

            return "https://discord.com/oauth2/authorize"
                + $"?client_id={Uri.EscapeDataString(config.ClientId)}"
                + "&response_type=code"
                + "&scope=identify"
                + $"&redirect_uri={Uri.EscapeDataString(config.RedirectUri)}"
                + $"&state={state}";
        }

        /// <summary>
        /// Anonymous OAuth callback: validates + consumes the state, exchanges the code (client
        /// secret stays server-side), reads /users/@me and stores the link. Always returns a small
        /// HTML page that tries to bounce back into the app via the gamehubz:// scheme.
        /// </summary>
        public async Task<string> HandleCallbackAsync(string? code, string? state)
        {
            if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(state))
                return BuildResultHtml(false, "The Discord sign-in was cancelled or incomplete.");

            string cacheKey = StateCachePrefix + state;
            var userIdRaw = await cacheService.GetAsync<string>(cacheKey);
            if (string.IsNullOrEmpty(userIdRaw) || !Guid.TryParse(userIdRaw, out var userId))
                return BuildResultHtml(false, "This link has expired. Start again from the GameHubz app.");

            // Consume the state immediately — it must never validate twice.
            await cacheService.RemoveAsync(cacheKey);

            try
            {
                var identity = await ExchangeCodeForIdentityAsync(code);
                if (identity == null)
                    return BuildResultHtml(false, "Discord didn't confirm the sign-in. Try again from the GameHubz app.");

                var user = await this.AppUnitOfWork.UserRepository.GetByIdOrThrowIfNull(userId);
                user.DiscordUserId = identity.Value.Id;
                user.DiscordUsername = identity.Value.Username;

                // Anonymous endpoint → no caller token; audit stamps fall back to the system user.
                await this.AppUnitOfWork.UserRepository.UpdateEntity(user, this.anonymousUserContextReader);
                await this.SaveAsync();

                await cacheService.RemoveAsync($"user_profile:{userId}");

                // Legacy Discord usernames may contain arbitrary characters — encode before
                // interpolating into the HTML page.
                string safeUsername = System.Net.WebUtility.HtmlEncode(identity.Value.Username);
                return BuildResultHtml(true, $"Discord account @{safeUsername} connected.");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Discord link callback failed for user {UserId}.", userId);
                return BuildResultHtml(false, "Something went wrong while connecting Discord. Try again from the GameHubz app.");
            }
        }

        public async Task UnlinkAsync()
        {
            var caller = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();

            var user = await this.AppUnitOfWork.UserRepository.GetByIdOrThrowIfNull(caller.UserId);
            user.DiscordUserId = null;
            user.DiscordUsername = null;

            await this.AppUnitOfWork.UserRepository.UpdateEntity(user, this.UserContextReader);
            await this.SaveAsync();

            await cacheService.RemoveAsync($"user_profile:{caller.UserId}");
        }

        public async Task SetDmEnabledAsync(bool enabled)
        {
            var caller = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();

            var user = await this.AppUnitOfWork.UserRepository.GetByIdOrThrowIfNull(caller.UserId);
            user.DiscordDmEnabled = enabled;

            await this.AppUnitOfWork.UserRepository.UpdateEntity(user, this.UserContextReader);
            await this.SaveAsync();

            await cacheService.RemoveAsync($"user_profile:{caller.UserId}");
        }

        // Exchanges the authorization code and reads the Discord identity. The access/refresh
        // tokens are deliberately discarded — identify is all we ever need, and storing tokens
        // we won't use again would only widen the blast radius of a leak.
        private async Task<(string Id, string Username)?> ExchangeCodeForIdentityAsync(string code)
        {
            var client = httpClientFactory.CreateClient("DiscordOAuth");

            using var tokenResponse = await client.PostAsync("oauth2/token", new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = config.ClientId,
                ["client_secret"] = config.ClientSecret,
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["redirect_uri"] = config.RedirectUri,
            }));

            if (!tokenResponse.IsSuccessStatusCode)
            {
                logger.LogWarning("Discord token exchange returned {StatusCode}: {Body}",
                    tokenResponse.StatusCode, await tokenResponse.Content.ReadAsStringAsync());
                return null;
            }

            using var tokenDoc = JsonDocument.Parse(await tokenResponse.Content.ReadAsStringAsync());
            var accessToken = tokenDoc.RootElement.GetProperty("access_token").GetString();
            if (string.IsNullOrEmpty(accessToken))
                return null;

            using var meRequest = new HttpRequestMessage(HttpMethod.Get, "users/@me");
            meRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            using var meResponse = await client.SendAsync(meRequest);

            if (!meResponse.IsSuccessStatusCode)
            {
                logger.LogWarning("Discord /users/@me returned {StatusCode}.", meResponse.StatusCode);
                return null;
            }

            using var meDoc = JsonDocument.Parse(await meResponse.Content.ReadAsStringAsync());
            var id = meDoc.RootElement.GetProperty("id").GetString();
            var username = meDoc.RootElement.TryGetProperty("username", out var usernameElement)
                ? usernameElement.GetString()
                : null;

            return id == null ? null : (id, username ?? "");
        }

        private string BuildResultHtml(bool success, string message)
        {
            string appLink = $"{shareLinksConfig.AppScheme}://";
            string icon = success ? "✓" : "✕";
            string iconColor = success ? "#10B981" : "#EF4444";
            string title = success ? "Discord connected" : "Connection failed";

            return $@"<!doctype html>
<html>
<head>
<meta charset=""utf-8"">
<meta name=""viewport"" content=""width=device-width, initial-scale=1"">
<title>GameHubz</title>
<style>
    body {{ margin: 0; min-height: 100vh; display: flex; align-items: center; justify-content: center;
           background: #0A0F1E; color: #fff; font-family: -apple-system, Segoe UI, Roboto, sans-serif; }}
    .card {{ text-align: center; padding: 40px 28px; max-width: 340px; }}
    .icon {{ font-size: 48px; color: {iconColor}; }}
    h1 {{ font-size: 20px; margin: 16px 0 8px; }}
    p {{ color: #94A3B8; font-size: 14px; line-height: 1.5; margin: 0 0 24px; }}
    a {{ display: inline-block; background: #5865F2; color: #fff; text-decoration: none;
        padding: 12px 24px; border-radius: 14px; font-weight: 700; font-size: 14px; }}
</style>
</head>
<body>
<div class=""card"">
    <div class=""icon"">{icon}</div>
    <h1>{title}</h1>
    <p>{message}<br>Return to the GameHubz app to continue.</p>
    <a href=""{appLink}"">Open GameHubz</a>
</div>
<script>setTimeout(function () {{ window.location.href = ""{appLink}""; }}, 800);</script>
</body>
</html>";
        }
    }
}
