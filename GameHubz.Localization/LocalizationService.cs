using System.Collections.Concurrent;
using System.Resources;
using GameHubz.DataModels.Consts;
using GameHubz.Localization.Resources;
using GameHubz.Logic.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Primitives;

namespace GameHubz.Localization
{
    /// <summary>
    /// Resolves user-facing strings for the language of the CURRENT request.
    /// <para>
    /// This is registered as a singleton and used from ~100 services, so the language can
    /// never be captured in the constructor: it is read per call. <see cref="IHttpContextAccessor"/>
    /// is safe to hold from a singleton because it resolves the context through an AsyncLocal,
    /// not through captured state.
    /// </para>
    /// <para>
    /// Anything running outside a request — background tasks, queue consumers, and push
    /// notifications, which must speak the RECIPIENT's language rather than the caller's —
    /// has no HttpContext to read. Those paths use the explicit
    /// <see cref="this[string, string]"/> overload instead.
    /// </para>
    /// </summary>
    public class LocalizationService : ILocalizationService
    {
        /// <summary>Header the mobile client sends on every request (see api.ts).</summary>
        private const string LanguageHeader = "Language";

        private static readonly ResourceManager EnglishResources = TranslationEN.ResourceManager;

        private static readonly ResourceManager SpanishResources = new ResourceManager(
            "GameHubz.Localization.Resources.TranslationES",
            typeof(LocalizationService).Assembly);

        private static readonly ResourceManager PortugueseResources = new ResourceManager(
            "GameHubz.Localization.Resources.TranslationPT",
            typeof(LocalizationService).Assembly);

        private static readonly ResourceManager PolishResources = new ResourceManager(
            "GameHubz.Localization.Resources.TranslationPL",
            typeof(LocalizationService).Assembly);

        /// <summary>ResourceManager lookups are cheap but the switch below is hit on every string.</summary>
        private static readonly ConcurrentDictionary<string, ResourceManager> ManagerCache = new();

        private readonly IHttpContextAccessor? httpContextAccessor;
        private readonly string configuredLanguage;

        /// <param name="httpContextAccessor">
        /// Optional so the service can also be constructed outside the web host (tests, tooling).
        /// When absent, every lookup falls back to the configured language.
        /// </param>
        public LocalizationService(IConfiguration configuration, IHttpContextAccessor? httpContextAccessor = null)
        {
            this.httpContextAccessor = httpContextAccessor;

            // appsettings.json (production) does not define "Language" at all, so this is
            // routinely null — English is the fallback, matching the previous behaviour.
            this.configuredLanguage = Languages.Normalize(configuration.GetValue<string>("Language")) ?? Languages.English;
        }

        /// <summary>String in the current request's language.</summary>
        public string this[string key] => Resolve(key, this.CurrentLanguage);

        /// <summary>
        /// String in an explicitly chosen language. Used where the reader is not the caller —
        /// push notifications and e-mails are written in the recipient's language.
        /// </summary>
        public string this[string key, string? language] => Resolve(key, Languages.Normalize(language) ?? this.configuredLanguage);

        public string PropertyIsEmptyMessage(string propertyName)
        {
            return string.Format(this["CommonValidator.PropertyIsEmpty"], propertyName);
        }

        public string CannotDeleteEntityAlreadyAddedTo<TChild, TParent>(Guid childEntityId)
        {
            return string.Format(this["CommonValidator.CannotDeleteEntityAlreadyAddedTo"], nameof(TChild), childEntityId, nameof(TParent));
        }

        public string PropertyValueAlreadyExists(string objectName, string propertyName)
        {
            return string.Format(this["CommonValidator.PropertyValueAlreadyExists"], objectName, propertyName);
        }

        /// <summary>Request header when there is a request, otherwise the configured default.</summary>
        public string CurrentLanguage => this.ReadLanguageHeader() ?? this.configuredLanguage;

        private string? ReadLanguageHeader()
        {
            HttpRequest? request = this.httpContextAccessor?.HttpContext?.Request;

            if (request is null)
            {
                return null;
            }

            if (!request.Headers.TryGetValue(LanguageHeader, out StringValues values) || values.Count == 0)
            {
                return null;
            }

            return Languages.Normalize(values[0]);
        }

        private static string Resolve(string key, string language)
        {
            ResourceManager manager = ManagerCache.GetOrAdd(language, ManagerFor);

            // A key missing from the translated set falls back to English rather than to the
            // key name, so a partially translated resx never leaks "Exception.EmptyEmail".
            string? value = manager.GetString(key) ?? EnglishResources.GetString(key);

            return value ?? "(no translation)";
        }

        private static ResourceManager ManagerFor(string language)
            => language switch
            {
                Languages.Spanish => SpanishResources,
                Languages.Portuguese => PortugueseResources,
                Languages.Polish => PolishResources,
                // "sr" is the legacy header default and has no resource set; it has always
                // rendered English, and still does.
                _ => EnglishResources,
            };
    }
}
