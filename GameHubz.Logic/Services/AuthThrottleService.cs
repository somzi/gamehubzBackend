using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace GameHubz.Logic.Services
{
    /// <summary>
    /// Abuse throttling for the anonymous /api/Auth surface: password guessing against a single
    /// account, credential stuffing across many, and reset-code spamming at someone's mailbox.
    /// </summary>
    /// <remarks>
    /// Deliberately NOT the built-in ASP.NET Core rate limiter. That one partitions in process
    /// memory — it resets on every deploy and counts separately per container — and it would have
    /// to key on the connection's remote address, which behind our reverse proxy is the proxy's own
    /// container IP. Every user would land in one shared bucket, so the first handful of mistyped
    /// passwords anywhere would lock out the entire user base at once. These counters live in Redis
    /// (shared across instances, survives restarts) and key on the account being targeted instead,
    /// mirroring the OTP guard already in <see cref="PasswordManagementService"/>.
    ///
    /// Only failures are counted and a success clears the counter, so someone fumbling their
    /// password a few times is never held back once they get it right.
    /// </remarks>
    public class AuthThrottleService
    {
        // Per-account failure budget: high enough that a real person mistyping never reaches it,
        // low enough that guessing one account's password is hopeless. This is the control that
        // actually holds — it keys on the account, which an attacker can't rotate away from.
        private const int MaxLoginFailuresPerAccount = 10;
        private static readonly TimeSpan LoginFailureWindow = TimeSpan.FromMinutes(15);

        // Per-IP budget across all accounts — the credential-stuffing shape (many accounts, one
        // password), which the per-account counter alone can't see. Coarse by nature: the address
        // comes from X-Forwarded-For, which a determined attacker can rotate, so treat this as a
        // net for the cheap noisy case rather than a hard boundary.
        private const int MaxLoginFailuresPerIp = 50;
        private static readonly TimeSpan LoginIpFailureWindow = TimeSpan.FromMinutes(15);

        // Reset codes: caps how often one mailbox can be targeted, so the endpoint can't be used to
        // bomb an inbox or to keep churning a victim's pending OTP.
        private const int MaxResetSendsPerAccount = 5;
        private static readonly TimeSpan ResetSendWindow = TimeSpan.FromMinutes(30);

        private readonly ICacheService cacheService;
        private readonly IHttpContextAccessor httpContextAccessor;
        private readonly ILogger<AuthThrottleService> logger;

        public AuthThrottleService(
            ICacheService cacheService,
            IHttpContextAccessor httpContextAccessor,
            ILogger<AuthThrottleService> logger)
        {
            this.cacheService = cacheService;
            this.httpContextAccessor = httpContextAccessor;
            this.logger = logger;
        }

        /// <summary>
        /// True when this account (or the caller's address) has burned its failure budget. The
        /// caller must answer exactly as it would for a wrong password — never with a distinct
        /// status or message — so the endpoint doesn't tell an attacker which accounts are under
        /// attack, and so existing clients need no change to handle a new response shape.
        /// </summary>
        public async Task<bool> IsLoginBlockedAsync(string email)
        {
            return await SafeAsync(async () =>
            {
                if (await cacheService.GetCounterAsync(LoginAccountKey(email)) >= MaxLoginFailuresPerAccount)
                {
                    return true;
                }

                string? ip = ResolveDistinguishableClientIp();

                return ip != null
                    && await cacheService.GetCounterAsync(LoginIpKey(ip)) >= MaxLoginFailuresPerIp;
            });
        }

        public async Task RegisterLoginFailureAsync(string email)
        {
            await SafeAsync(async () =>
            {
                await cacheService.IncrementAsync(LoginAccountKey(email), LoginFailureWindow);

                string? ip = ResolveDistinguishableClientIp();
                if (ip != null)
                {
                    await cacheService.IncrementAsync(LoginIpKey(ip), LoginIpFailureWindow);
                }

                return true;
            });
        }

        /// <summary>
        /// Clears the account's failure budget after a successful sign-in. The per-IP counter is
        /// deliberately left alone: on a shared address one attacker's own valid account would
        /// otherwise wipe the evidence of everything they just tried against everyone else's.
        /// </summary>
        public async Task ClearLoginFailuresAsync(string email)
        {
            await SafeAsync(async () =>
            {
                await cacheService.RemoveAsync(LoginAccountKey(email));
                return true;
            });
        }

        public async Task<bool> IsResetSendBlockedAsync(string email)
        {
            return await SafeAsync(async () =>
                await cacheService.GetCounterAsync(ResetSendKey(email)) >= MaxResetSendsPerAccount);
        }

        public async Task RegisterResetSendAsync(string email)
        {
            await SafeAsync(async () =>
            {
                await cacheService.IncrementAsync(ResetSendKey(email), ResetSendWindow);
                return true;
            });
        }

        /// <summary>
        /// The client's address, but ONLY when the proxy actually tells us who the client is.
        /// With X-Forwarded-For absent every request looks like it came from the proxy container,
        /// so a per-IP counter would lump the whole user base into a single bucket and lock
        /// everybody out the moment a few people mistyped. Returning null there skips IP throttling
        /// entirely — the per-account counter is the one that matters anyway.
        /// </summary>
        private string? ResolveDistinguishableClientIp()
        {
            string? forwarded = httpContextAccessor.HttpContext?.Request.Headers["X-Forwarded-For"].ToString();

            if (string.IsNullOrWhiteSpace(forwarded))
            {
                return null;
            }

            string first = forwarded.Split(',')[0].Trim();

            return string.IsNullOrWhiteSpace(first) ? null : first;
        }

        /// <summary>
        /// Throttling must never be able to take sign-in down with it. Login doesn't touch Redis
        /// today, so without this a cache blip would turn into a total auth outage — a far worse
        /// outcome than the guard being briefly unavailable. Fail open and log it.
        /// </summary>
        private async Task<bool> SafeAsync(Func<Task<bool>> action)
        {
            try
            {
                return await action();
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Auth throttling is unavailable (cache error); allowing the request through.");
                return false;
            }
        }

        private static string LoginAccountKey(string email) => $"auth:login_fail:acct:{Normalize(email)}";

        private static string LoginIpKey(string ip) => $"auth:login_fail:ip:{ip}";

        private static string ResetSendKey(string email) => $"auth:reset_send:{Normalize(email)}";

        private static string Normalize(string email) => (email ?? string.Empty).Trim().ToLowerInvariant();
    }
}
