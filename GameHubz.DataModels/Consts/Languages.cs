namespace GameHubz.DataModels.Consts
{
    public class Languages
    {
        public const string Serbian = "sr";
        public const string English = "en";

        public const string Spanish = "es";

        /// <summary>
        /// Reduces a language tag to its bare code: "es-419", "ES" and " es " all become "es".
        /// Returns null for a blank value so callers can tell "not specified" apart from
        /// "specified as something we don't recognise" — an unrecognised tag is returned as-is
        /// and is expected to fall back to English wherever it is used.
        /// </summary>
        public static string? Normalize(string? language)
        {
            if (string.IsNullOrWhiteSpace(language))
            {
                return null;
            }

            string trimmed = language.Trim();
            int dash = trimmed.IndexOf('-');

            if (dash > 0)
            {
                trimmed = trimmed[..dash];
            }

            return trimmed.ToLowerInvariant();
        }

        /// <summary>
        /// Narrows any tag to a language we can actually render. Used before persisting a user's
        /// choice: an unknown code stored on the profile would render English on every push
        /// anyway, so storing English instead keeps the column meaningful.
        /// <para>"sr" is the legacy default and has no resource set, so it narrows to English too.</para>
        /// </summary>
        public static string ToSupported(string? language)
            => Normalize(language) == Spanish ? Spanish : English;
    }
}
