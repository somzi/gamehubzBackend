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
    }
}
