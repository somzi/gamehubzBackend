using System.Resources;
using Template.Localization.Resources;
using Template.Logic.Interfaces;
using Microsoft.Extensions.Configuration;

namespace Template.Localization
{
    public class LocalizationService : ILocalizationService
    {
        private readonly ResourceManager resourceManager;
        private readonly ResourceManager fallbackResourceManager;

        public LocalizationService(IConfiguration configuration)
        {
            this.fallbackResourceManager = TranslationEN.ResourceManager;

            string language = configuration.GetValue<string>("Language")!;

            this.resourceManager = GetResourceManager(language);
        }

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

        public string this[string key]
        {
            get
            {
                string? value = this.resourceManager.GetString(key);

                value ??= this.fallbackResourceManager.GetString(key);

                return value ?? "(no translation)";
            }
        }

        private static ResourceManager GetResourceManager(string language)
            => language switch
            {
                "sr" => TranslationEN.ResourceManager,
                _ => TranslationEN.ResourceManager,
            };
    }
}