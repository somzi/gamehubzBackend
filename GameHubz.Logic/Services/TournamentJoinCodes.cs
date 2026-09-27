namespace GameHubz.Logic.Services
{
    /// <summary>
    /// The rules around a private tournament's six-digit join code, shared by every door into one:
    /// looking a tournament up by its code (TournamentService.ResolveJoinCode), a solo registration
    /// (TournamentRegistrationService) and creating a team (TournamentTeamService).
    /// </summary>
    /// <remarks>
    /// All three share one attempt budget per account. Private tournaments are listed like any
    /// other, so their ids are no secret — the registration endpoints are as good a place to guess
    /// codes as the lookup, and a budget per endpoint would just triple an attacker's allowance.
    ///
    /// Every check of a well-formed code spends one attempt, and spends it atomically BEFORE the
    /// code is compared (Redis INCR hands each request its own count). Reading the counter first and
    /// incrementing on a miss afterwards let any number of parallel requests pass the read together
    /// — forty concurrent guesses all got through in review. Hits are counted as well: refunding
    /// them would need a decrement for no real gain, since an honest player spends two or three
    /// checks per tournament they join (look the code up, then register with it).
    ///
    /// Six digits is 900 000 codes; twenty checks per quarter hour makes walking that space from one
    /// account hopeless, while a person fumbling a code they were sent never gets near the limit.
    /// Counted per account, not per IP, for the reason AuthThrottleService spells out (behind our
    /// proxy every client shares one address).
    /// </remarks>
    public static class TournamentJoinCodes
    {
        private const int MaxAttempts = 20;
        private static readonly TimeSpan AttemptWindow = TimeSpan.FromMinutes(15);

        /// <summary>
        /// Digits only, exactly six of them. People paste codes out of chat messages, so spaces and
        /// dashes are forgiven; anything else is simply not a code.
        /// </summary>
        public static string? Normalize(string? code)
        {
            if (string.IsNullOrWhiteSpace(code)) return null;

            string digits = new string(code.Where(c => !char.IsWhiteSpace(c) && c != '-').ToArray());

            return digits.Length == 6 && digits.All(char.IsAsciiDigit) ? digits : null;
        }

        /// <summary>
        /// Spends one attempt from the caller's budget, or throws when it is used up. Call it before
        /// comparing a code, never after — see the remarks on the class.
        /// </summary>
        public static async Task ConsumeAttemptAsync(ICacheService cacheService, ILocalizationService localization, Guid userId)
        {
            long used = await SafeCounterAsync(() => cacheService.IncrementAsync(AttemptKey(userId), AttemptWindow));

            if (used > MaxAttempts)
            {
                throw new BusinessRuleException(localization["BusinessRule.JoinCodeTooManyAttempts"]);
            }
        }

        /// <summary>
        /// The registration-side gate: throws unless <paramref name="code"/> is this private
        /// tournament's code. No-op for public tournaments. Callers let managers through before
        /// calling — they hold the code anyway, and must never be locked out of their own event.
        /// A missing code is a prompt, not a guess, so it costs nothing.
        /// </summary>
        public static async Task EnsureCanEnterAsync(
            TournamentEntity tournament,
            string? code,
            Guid userId,
            ICacheService cacheService,
            ILocalizationService localization)
        {
            if (!tournament.IsPrivate) return;

            string? normalized = Normalize(code);
            if (normalized == null)
            {
                throw new BusinessRuleException(localization["BusinessRule.JoinCodeRequired"]);
            }

            await ConsumeAttemptAsync(cacheService, localization, userId);

            if (!string.Equals(normalized, tournament.JoinCode, StringComparison.Ordinal))
            {
                throw new BusinessRuleException(localization["BusinessRule.JoinCodeWrong"]);
            }
        }

        private static string AttemptKey(Guid userId) => $"join_code_attempts:{userId}";

        // The throttle is a guard, not a dependency: with Redis down the checks still run and only
        // the budget is lost (fail open), exactly like AuthThrottleService.
        private static async Task<long> SafeCounterAsync(Func<Task<long>> action)
        {
            try
            {
                return await action();
            }
            catch
            {
                return 0;
            }
        }
    }
}
