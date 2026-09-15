namespace GameHubz.Logic.Interfaces
{
    /// <summary>
    /// Who a push is going to, and which language they read the app in.
    /// <para>
    /// A push is written for its RECIPIENT, not for whoever triggered it — the request's
    /// Language header belongs to the actor, so it must never decide the wording here.
    /// <see cref="Language"/> comes from <c>UserEntity.Language</c>; null means "unknown",
    /// which resolves to the server default.
    /// </para>
    /// <para>
    /// <see cref="UserId"/> is the account the notification is for. When it is set the notification is
    /// also written to that user's inbox — even with an empty <see cref="PushToken"/>, which is exactly
    /// the user who would otherwise never hear about it. Build those with <see cref="ForUser"/>.
    /// </para>
    /// </summary>
    public readonly record struct PushRecipient(string PushToken, string? Language, Guid? UserId = null)
    {
        /// <summary>Always gets an inbox row; gets a push too when the user has a token.</summary>
        public static PushRecipient ForUser(Guid userId, string? pushToken, string? language)
            => new(pushToken ?? string.Empty, language, userId);
    }

    /// <summary>One notification of a batch: its own recipients, wording and payload.</summary>
    public sealed record LocalizedPush(IReadOnlyCollection<PushRecipient> Recipients, PushText Title, PushText Body, object? Data = null);

    /// <summary>
    /// One line of a push — either a translation key, or literal text that must not be
    /// translated (a username, team name, hub name or tournament name).
    /// </summary>
    public readonly record struct PushText
    {
        private PushText(string? key, string? literal, object[]? args)
        {
            this.ResourceKey = key;
            this.Literal = literal;
            this.Args = args;
        }

        public string? ResourceKey { get; }

        public string? Literal { get; }

        public object[]? Args { get; }

        /// <summary>Translated per recipient. <paramref name="args"/> fill {0}, {1}, … in the resource.</summary>
        public static PushText FromKey(string key, params object[] args) => new(key, null, args);

        /// <summary>Passed through verbatim — for names and other data that is not language-dependent.</summary>
        public static PushText FromLiteral(string text) => new(null, text, null);

        /// <summary>Resolves this line for one recipient's language.</summary>
        public string Resolve(ILocalizationService localization, string? language)
        {
            if (this.ResourceKey is null)
            {
                return this.Literal ?? string.Empty;
            }

            // Always the explicit-language overload, including when the recipient's language is
            // unknown. The indexer WITHOUT a language reads the current request's header, which
            // belongs to whoever triggered the push rather than to the person who will read it —
            // one Spanish organiser's action would otherwise word everyone else's push in Spanish.
            // A null here resolves to the server default (English), which is what an account with
            // no stored language is.
            string template = localization[this.ResourceKey, language];

            return this.Args is { Length: > 0 }
                ? string.Format(template, this.Args)
                : template;
        }
    }
}
