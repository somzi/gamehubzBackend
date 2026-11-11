using GameHubz.DataModels.Config;
using GameHubz.DataModels.Enums;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GameHubz.Logic.Services
{
    /// <summary>
    /// Links a Twitch or Kick channel to a GameHubz account, the same way the Discord link works: the app
    /// opens the authorize url in the system browser, the platform comes back to an anonymous callback,
    /// and a one-time state in Redis ties that callback to the account that started it. Unlike Discord,
    /// the tokens are kept (encrypted): each broadcast needs a fresh stream key read as the channel owner.
    /// </summary>
    public class StreamAccountService : AppBaseService
    {
        private const string StateCachePrefix = "stream:link_state:";
        private static readonly TimeSpan StateTtl = TimeSpan.FromMinutes(10);

        // Refresh a little before the platform would refuse the token.
        private static readonly TimeSpan RefreshMargin = TimeSpan.FromMinutes(2);

        private readonly ICacheService cacheService;
        private readonly IEnumerable<IStreamAccountProvider> providers;
        private readonly StreamTokenProtector protector;
        private readonly AnonymousUserContextReader anonymousUserContextReader;
        private readonly ShareLinksConfig shareLinksConfig;
        private readonly ILogger<StreamAccountService> logger;

        public StreamAccountService(
            IUnitOfWorkFactory unitOfWorkFactory,
            IUserContextReader userContextReader,
            ILocalizationService localizationService,
            ICacheService cacheService,
            IEnumerable<IStreamAccountProvider> providers,
            StreamTokenProtector protector,
            AnonymousUserContextReader anonymousUserContextReader,
            IOptions<ShareLinksConfig> shareLinksOptions,
            ILogger<StreamAccountService> logger)
            : base(unitOfWorkFactory.CreateAppUnitOfWork(), userContextReader, localizationService)
        {
            this.cacheService = cacheService;
            this.providers = providers;
            this.protector = protector;
            this.anonymousUserContextReader = anonymousUserContextReader;
            this.shareLinksConfig = shareLinksOptions.Value;
            this.logger = logger;
        }

        public async Task<StreamAccountsDto> GetMine()
        {
            var caller = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();
            var accounts = await this.AppUnitOfWork.UserStreamAccountRepository.GetByUserId(caller.UserId);

            return new StreamAccountsDto
            {
                Accounts = accounts.Select(ToDto).ToList(),
                AvailablePlatforms = this.providers
                    .Where(p => p.IsConfigured && this.protector.IsConfigured)
                    .Select(p => p.Platform)
                    .ToList(),
            };
        }

        /// <summary>Creates the one-time state and returns the platform's authorize url to open.</summary>
        public async Task<string> StartLinkAsync(SocialType platform)
        {
            var caller = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();
            var provider = this.ConfiguredProviderOrThrow(platform);

            string state = StreamTokenProtector.NewToken();
            string? verifier = provider.UsesPkce ? StreamTokenProtector.NewToken() + StreamTokenProtector.NewToken() : null;

            // The callback is anonymous: this entry is the only thing binding the returning code to a
            // GameHubz account (CSRF / session fixation), and it carries the PKCE verifier server-side.
            await this.cacheService.SetAsync(StateCachePrefix + state, new LinkState
            {
                UserId = caller.UserId,
                Platform = (int)platform,
                CodeVerifier = verifier,
            }, StateTtl);

            return provider.BuildAuthorizeUrl(state, verifier == null ? null : StreamTokenProtector.CodeChallenge(verifier));
        }

        /// <summary>
        /// Anonymous OAuth callback: validates + consumes the state, exchanges the code, reads the channel
        /// and stores the link. Always returns a small HTML page that bounces back into the app.
        /// </summary>
        public async Task<string> HandleCallbackAsync(SocialType platform, string? code, string? state, string? error)
        {
            string name = PlatformName(platform);

            if (!string.IsNullOrEmpty(error) || string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(state))
                return this.BuildResultHtml(platform, false, $"The {name} sign-in was cancelled or incomplete.");

            string cacheKey = StateCachePrefix + state;
            var linkState = await this.cacheService.GetAsync<LinkState>(cacheKey);
            if (linkState == null || linkState.Platform != (int)platform)
                return this.BuildResultHtml(platform, false, "This link has expired. Start again from the GameHubz app.");

            // Consume the state immediately — it must never validate twice.
            await this.cacheService.RemoveAsync(cacheKey);

            var provider = this.providers.FirstOrDefault(p => p.Platform == platform && p.IsConfigured);
            if (provider == null || !this.protector.IsConfigured)
                return this.BuildResultHtml(platform, false, $"{name} linking is not available right now.");

            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));

                var tokens = await provider.ExchangeCodeAsync(code, linkState.CodeVerifier, cts.Token);
                if (tokens == null)
                    return this.BuildResultHtml(platform, false, $"{name} didn't confirm the sign-in. Try again from the GameHubz app.");

                var identity = await provider.GetIdentityAsync(tokens.AccessToken, cts.Token);
                if (identity == null)
                    return this.BuildResultHtml(platform, false, $"Couldn't read your {name} channel. Try again from the GameHubz app.");

                // One channel maps to one GameHubz account — otherwise two accounts could broadcast to it
                // and the ownership the link proves would mean nothing. Re-linking your own is fine.
                var owner = await this.AppUnitOfWork.UserStreamAccountRepository.GetByExternalUserId(platform, identity.ExternalUserId);
                if (owner != null && owner.UserId != linkState.UserId)
                    return this.BuildResultHtml(platform, false, $"This {name} channel is already linked to another GameHubz account. Unlink it there first.");

                var now = DateTime.UtcNow;
                var account = await this.AppUnitOfWork.UserStreamAccountRepository.GetByUserAndPlatform(linkState.UserId, platform);
                bool isNew = account == null;
                account ??= new UserStreamAccountEntity { UserId = linkState.UserId, Platform = platform };

                account.ExternalUserId = identity.ExternalUserId;
                account.Login = identity.Login;
                account.DisplayName = identity.DisplayName;
                account.AccessToken = this.protector.Protect(tokens.AccessToken);
                account.RefreshToken = this.protector.Protect(tokens.RefreshToken);
                account.AccessTokenExpiresOn = now.AddSeconds(tokens.ExpiresInSeconds);
                account.Scopes = tokens.Scopes;
                account.NeedsRelink = false;
                account.LinkedOn = now;

                // Anonymous endpoint → no caller token; audit stamps fall back to the system user.
                if (isNew)
                    await this.AppUnitOfWork.UserStreamAccountRepository.AddEntity(account, this.anonymousUserContextReader);
                else
                    await this.AppUnitOfWork.UserStreamAccountRepository.UpdateEntity(account, this.anonymousUserContextReader);

                await this.SyncSocialAsync(linkState.UserId, platform, identity.Login);
                await this.SaveAsync();

                await this.cacheService.RemoveAsync($"user_profile:{linkState.UserId}");

                string safeName = System.Net.WebUtility.HtmlEncode(identity.DisplayName ?? identity.Login);
                return this.BuildResultHtml(platform, true, $"{name} channel {safeName} connected.");
            }
            catch (Exception ex)
            {
                this.logger.LogError(ex, "{Platform} link callback failed for user {UserId}.", name, linkState.UserId);
                return this.BuildResultHtml(platform, false, $"Something went wrong while connecting {name}. Try again from the GameHubz app.");
            }
        }

        public async Task UnlinkAsync(SocialType platform)
        {
            var caller = await this.UserContextReader.GetTokenUserInfoFromContextThrowIfNull();

            var account = await this.AppUnitOfWork.UserStreamAccountRepository.GetByUserAndPlatform(caller.UserId, platform);
            if (account == null) return;

            var provider = this.providers.FirstOrDefault(p => p.Platform == platform);
            if (provider != null)
            {
                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                    await provider.RevokeAsync(
                        this.protector.Unprotect(account.AccessToken),
                        this.protector.Unprotect(account.RefreshToken),
                        cts.Token);
                }
                catch (Exception ex)
                {
                    // The grant can also be removed on the platform's own settings page; the local row
                    // (and with it the only copy of the tokens) goes either way.
                    this.logger.LogInformation(ex, "Revoking the {Platform} grant failed on unlink.", PlatformName(platform));
                }
            }

            // Hard delete: nothing should keep the tokens once the user unlinks.
            await this.AppUnitOfWork.UserStreamAccountRepository.HardDeleteEntity(account);
            await this.SaveAsync();
        }

        /// <summary>
        /// A usable access token for the account, refreshed (and the new pair stored) when it is about to
        /// expire — or right away with <paramref name="forceRefresh"/>, after the platform refused the
        /// current one although its expiry hadn't come (revoked or invalidated early).
        ///
        /// Only a refresh the platform refuses marks the account for re-link. A platform that is down or
        /// rate-limiting leaves the link alone and fails with a "try again" message.
        /// </summary>
        public async Task<string> GetAccessTokenAsync(UserStreamAccountEntity account, CancellationToken ct, bool forceRefresh = false)
        {
            if (account.NeedsRelink)
                throw new BusinessRuleException(this.LocalizationService["BusinessRule.StreamAccountNeedsRelink"]);

            string? access = this.protector.Unprotect(account.AccessToken);
            if (!forceRefresh && access != null && account.AccessTokenExpiresOn > DateTime.UtcNow.Add(RefreshMargin))
                return access;

            var provider = this.ProviderOrThrow(account.Platform);
            string? refresh = this.protector.Unprotect(account.RefreshToken);

            StreamOAuthTokens? tokens;
            try
            {
                tokens = refresh == null ? null : await provider.RefreshAsync(refresh, ct);
            }
            catch (StreamPlatformUnavailableException)
            {
                throw new BusinessRuleException(this.LocalizationService["BusinessRule.BroadcastIngestUnavailable"]);
            }

            if (tokens == null)
            {
                await this.MarkNeedsRelinkAsync(account);
                throw new BusinessRuleException(this.LocalizationService["BusinessRule.StreamAccountNeedsRelink"]);
            }

            // Kick rotates refresh tokens: the new one must be stored or the next refresh fails.
            account.AccessToken = this.protector.Protect(tokens.AccessToken);
            account.RefreshToken = this.protector.Protect(tokens.RefreshToken);
            account.AccessTokenExpiresOn = DateTime.UtcNow.AddSeconds(tokens.ExpiresInSeconds);
            if (!string.IsNullOrEmpty(tokens.Scopes)) account.Scopes = tokens.Scopes;

            await this.AppUnitOfWork.UserStreamAccountRepository.UpdateEntity(account, this.UserContextReader);
            await this.SaveAsync();

            return tokens.AccessToken;
        }

        /// <summary>The platform no longer honours this link; the app asks the player to connect again.</summary>
        public async Task MarkNeedsRelinkAsync(UserStreamAccountEntity account)
        {
            account.NeedsRelink = true;
            await this.AppUnitOfWork.UserStreamAccountRepository.UpdateEntity(account, this.UserContextReader);
            await this.SaveAsync();
        }

        public IStreamAccountProvider ProviderOrThrow(SocialType platform)
        {
            return this.providers.FirstOrDefault(p => p.Platform == platform)
                ?? throw new BusinessRuleException(this.LocalizationService["BusinessRule.UnsupportedStreamPlatform"]);
        }

        public static string PlatformName(SocialType platform) => platform switch
        {
            SocialType.Twitch => "Twitch",
            SocialType.Kick => "Kick",
            _ => platform.ToString(),
        };

        public static string ChannelUrl(SocialType platform, string login) => platform switch
        {
            SocialType.Twitch => $"https://www.twitch.tv/{login}",
            SocialType.Kick => $"https://kick.com/{login}",
            _ => string.Empty,
        };

        private IStreamAccountProvider ConfiguredProviderOrThrow(SocialType platform)
        {
            var provider = this.ProviderOrThrow(platform);
            if (!provider.IsConfigured || !this.protector.IsConfigured)
                throw new BusinessRuleException(this.LocalizationService["BusinessRule.StreamLinkNotConfigured"]);
            return provider;
        }

        // The linked channel becomes the profile's social for that platform too, so the profile link and
        // the hand-attached stream flow show the verified channel.
        private async Task SyncSocialAsync(Guid userId, SocialType platform, string login)
        {
            var socials = await this.AppUnitOfWork.UserSocialRepository.GetByUserId(userId);
            var social = socials.FirstOrDefault(s => s.Type == platform);

            if (social == null)
            {
                await this.AppUnitOfWork.UserSocialRepository.AddEntity(new UserSocialEntity
                {
                    Type = platform,
                    Username = login,
                    UserId = userId,
                }, this.anonymousUserContextReader);
            }
            else if (!string.Equals(social.Username, login, StringComparison.OrdinalIgnoreCase))
            {
                social.Username = login;
                await this.AppUnitOfWork.UserSocialRepository.UpdateEntity(social, this.anonymousUserContextReader);
            }
        }

        private static StreamAccountDto ToDto(UserStreamAccountEntity account) => new()
        {
            Platform = account.Platform,
            Login = account.Login,
            DisplayName = account.DisplayName,
            ChannelUrl = ChannelUrl(account.Platform, account.Login),
            NeedsRelink = account.NeedsRelink,
            LinkedOn = account.LinkedOn,
        };

        private string BuildResultHtml(SocialType platform, bool success, string message)
        {
            string appLink = $"{this.shareLinksConfig.AppScheme}://";
            string name = PlatformName(platform);
            string icon = success ? "✓" : "✕";
            string iconColor = success ? "#10B981" : "#EF4444";
            string title = success ? $"{name} connected" : "Connection failed";
            string buttonColor = platform == SocialType.Kick ? "#53FC18" : "#9146FF";
            string buttonText = platform == SocialType.Kick ? "#0A0F1E" : "#fff";

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
    a {{ display: inline-block; background: {buttonColor}; color: {buttonText}; text-decoration: none;
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

        private sealed class LinkState
        {
            public Guid UserId { get; set; }
            public int Platform { get; set; }
            public string? CodeVerifier { get; set; }
        }
    }
}
