namespace GameHubz.Logic.Interfaces
{
    public interface ILocalizationService
    {
        string this[string key] { get; }

        /// <summary>
        /// String in an explicitly chosen language, for anything written for someone other
        /// than the caller — push notifications and e-mails go out in the RECIPIENT's
        /// language, and background tasks have no request to read a header from.
        /// </summary>
        string this[string key, string? language] { get; }

        /// <summary>
        /// The language of the current request, already normalized ("es-419" reads as "es").
        /// Falls back to the configured default outside a request. Use this only to RECORD a
        /// choice (stamping a new account); to render a string, index the service instead.
        /// </summary>
        string CurrentLanguage { get; }

        string PropertyIsEmptyMessage(string propertyName);

        string CannotDeleteEntityAlreadyAddedTo<TChild, TParent>(Guid childEntityId);

        string PropertyValueAlreadyExists(string objectName, string propertyName);
    }
}
