namespace GameHubz.Logic.Services
{
    /// <summary>
    /// The rules around a private tournament's six-digit join code, shared by every door into one:
    /// looking a tournament up by its code (TournamentService.ResolveJoinCode), a solo registration
    /// (TournamentRegistrationService), creating a team, and joining or requesting to join one
    /// (TournamentTeamService).
    /// </summary>
    /// <remarks>
    /// A player gets five WRONG codes per quarter hour; a correct code costs nothing, so looking a
    /// tournament up and then registering with the same code never eats into the five. All entry
    /// paths share the one budget per account: private tournaments are listed like any other, so
    /// their ids are no secret, and a budget per endpoint would multiply an attacker's allowance.
    ///
    /// Counting only misses must still hold under parallel requests. Reading the counter first and
    /// incrementing on a miss afterwards let any number of concurrent guesses pass the read together
    /// — forty got through in review. So every check reserves an attempt atomically BEFORE the code
    /// is compared (Redis INCR hands each request its own count, and anything past the limit is
    /// refused on the spot), and a check that turns out right gives its reservation back.
    ///
    /// Six digits is 900 000 codes; five wrong guesses per quarter hour makes walking that space
    /// from one account hopeless. Counted per account, not per IP, for the reason
    /// AuthThrottleService spells out (behind our proxy every client shares one address).
    /// </remarks>
    public static class TournamentJoinCodes
    {
        private const int MaxWrongCodes = 5;
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
        /// Reserves one attempt from the caller's budget, or throws when five wrong codes have
        /// already been spent. Call it before comparing a code, never after, and pair a correct
        /// result with <see cref="RefundAttemptAsync"/> — see the remarks on the class.
        /// </summary>
        public static async Task ConsumeAttemptAsync(ICacheService cacheService, ILocalizationService localization, Guid userId)
        {
            long used;
            try
            {
                used = await cacheService.IncrementAsync(AttemptKey(userId), AttemptWindow);
            }
            catch
            {
                // Without the shared counter there is no reliable limit across API instances.
                // Refuse code checks until it recovers instead of allowing unlimited guesses.
                throw new BusinessRuleException(localization["BusinessRule.JoinCodeUnavailable"], "join_code_unavailable");
            }

            if (used > MaxWrongCodes)
            {
                throw new BusinessRuleException(localization["BusinessRule.JoinCodeTooManyAttempts"], "join_code_limit");
            }
        }

        /// <summary>
        /// Gives back the attempt a correct code reserved. Best effort: if the counter can't be
        /// reached, the player loses one attempt — never the registration that just succeeded.
        /// </summary>
        public static async Task RefundAttemptAsync(ICacheService cacheService, Guid userId)
        {
            try
            {
                await cacheService.DecrementCounterAsync(AttemptKey(userId));
            }
            catch
            {
                // Swallowed on purpose — see the summary.
            }
        }

        /// <summary>
        /// The registration-side gate: throws unless <paramref name="code"/> is this private
        /// tournament's code. No-op for public tournaments. Callers let managers through before
        /// calling — they hold the code anyway, and must never be locked out of their own event.
        /// A missing code is a prompt, not a guess, so it costs nothing; a correct one is refunded.
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
                throw new BusinessRuleException(localization["BusinessRule.JoinCodeRequired"], "join_code_required");
            }

            await ConsumeAttemptAsync(cacheService, localization, userId);

            if (!string.Equals(normalized, tournament.JoinCode, StringComparison.Ordinal))
            {
                throw new BusinessRuleException(localization["BusinessRule.JoinCodeWrong"], "join_code_wrong");
            }

            await RefundAttemptAsync(cacheService, userId);
        }

        private static string AttemptKey(Guid userId) => $"join_code_attempts:{userId}";
    }
}
