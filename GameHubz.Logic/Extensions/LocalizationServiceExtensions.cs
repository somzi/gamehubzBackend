using GameHubz.DataModels.Consts;
using GameHubz.Logic.Interfaces;

namespace GameHubz.Logic.Extensions
{
    public static class LocalizationServiceExtensions
    {
        /// <summary>
        /// Resolves a string for someone who is not the caller — the reader of an e-mail or a
        /// notification.
        /// <para>
        /// Prefers the recipient's stored profile language. Falls back to the current request's
        /// language when the profile has none, which is the right answer for the flows where the
        /// recipient IS the person making the request (registering, asking for a reset code) and
        /// their profile predates the language picker.
        /// </para>
        /// </summary>
        public static string ForRecipient(this ILocalizationService localization, string? recipientLanguage, string key)
            => string.IsNullOrWhiteSpace(recipientLanguage)
                ? localization[key]
                : localization[key, recipientLanguage];

        /// <inheritdoc cref="ForRecipient(ILocalizationService, string?, string)"/>
        public static string ForRecipient(this ILocalizationService localization, string? recipientLanguage, string key, params object[] args)
            => string.Format(localization.ForRecipient(recipientLanguage, key), args);

        /// <summary>
        /// "1 match" / "3 matches" in the current request's language, with the count filled into {0}.
        /// <para>
        /// English, Spanish and Portuguese need only two forms, so these labels used to be a
        /// plain <c>count == 1 ? one : many</c> at each call site. The Slavic languages need three —
        /// "1 mecz / 2 mecze / 5 meczów", "1 meč / 2 meča / 5 mečeva", "1 матч / 2 матча /
        /// 5 матчей" — and a binary choice is visibly wrong for 2-4, so the arm is chosen per
        /// language here instead. A language with no separate few-form never resolves
        /// <paramref name="fewKey"/>, so those resources are simply a copy of the many-form.
        /// </para>
        /// <para>
        /// The three keys are passed in full rather than derived from a prefix because the two
        /// naming styles in the resx disagree — <c>Push.PlayedMatches.One</c> against
        /// <c>BusinessRule.MatchCountOne</c>.
        /// </para>
        /// </summary>
        public static string Plural(this ILocalizationService localization, int count, string oneKey, string fewKey, string manyKey)
            => string.Format(localization[PluralKey(localization.CurrentLanguage, count, oneKey, fewKey, manyKey)], count);

        /// <summary>
        /// The same label in an explicit language, for text embedded in something another user reads.
        /// <para>
        /// A null <paramref name="language"/> resolves exactly like <c>localization[key, null]</c> — the
        /// server default, NOT the caller's language — because that is how <c>PushText.Resolve</c> words
        /// the sentence around it. Falling back to the request header here would put a Polish
        /// organiser's "2 mecze" inside an English push.
        /// </para>
        /// </summary>
        public static string Plural(this ILocalizationService localization, int count, string oneKey, string fewKey, string manyKey, string? language)
            => string.Format(localization[PluralKey(Languages.Normalize(language), count, oneKey, fewKey, manyKey), language], count);

        private static string PluralKey(string? language, int count, string oneKey, string fewKey, string manyKey)
        {
            // CLDR "few" is the same rule in Polish, Serbian and Russian: ends in 2-4, except the
            // teens — 22 is "few", 12 is not.
            bool few = count % 10 >= 2 && count % 10 <= 4 && (count % 100 < 12 || count % 100 > 14);

            switch (language)
            {
                case Languages.Polish:
                    // Polish "one" is exactly 1: 21 is "many" ("21 meczów").
                    return count == 1 ? oneKey : few ? fewKey : manyKey;

                case Languages.Serbian:
                case Languages.Russian:
                    // "one" here is anything ending in 1 except 11 ("21 meč", "21 матч"), which is
                    // why these one-forms carry {0} instead of a literal 1.
                    return count % 10 == 1 && count % 100 != 11 ? oneKey : few ? fewKey : manyKey;

                default:
                    return count == 1 ? oneKey : manyKey;
            }
        }
    }
}
