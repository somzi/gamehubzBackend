using Microsoft.Extensions.Configuration;

namespace Template.Common.Extensions
{
    public static class ConfigurationExtensions
    {
        public static T GetValueThrowIfNull<T>(this IConfiguration configuration, string key)
        {
            T? value = configuration.GetValue<T>(key);

            return value == null
                ? throw new ArgumentNullException($"Config key not found: '{key}'")
                : value;
        }

        public static T GetValueOrDefaultValue<T>(this IConfiguration configuration, string key, T defaultValue)
        {
            T? value = configuration.GetValue<T>(key);

            return value == null
                ? defaultValue
                : value;
        }

        public static string GetStringThrowIfNull(this IConfiguration configuration, string key)
        {
            return configuration.GetValueThrowIfNull<string>(key);
        }
    }
}