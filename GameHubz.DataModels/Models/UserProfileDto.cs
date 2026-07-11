using System.Text.Json.Serialization;
using GameHubz.DataModels.Catalog;
using GameHubz.DataModels.Enums;

namespace GameHubz.DataModels.Models
{
    public class UserProfileDto
    {
        public Guid Id { get; set; }
        public string Username { get; set; } = string.Empty;
        public string Nickname { get; set; } = string.Empty;
        public RegionType Region { get; set; }

        /// <summary>ISO 3166-1 alpha-2 country code, or null if not set.</summary>
        public string? Country { get; set; }

        /// <summary>Display name for <see cref="Country"/> (from the catalog). Omitted when no country.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? CountryName => CountryCatalog.Get(Country)?.Name;

        /// <summary>Flag emoji for <see cref="Country"/> (from the catalog). Omitted when no country.</summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? CountryFlag => CountryCatalog.Get(Country)?.Flag;

        public List<UserSocialDto>? UserSocials { get; set; } = new();
        public string? AvatarUrl { get; set; }

        // Discord bot link status — populated only when the caller requests their OWN profile
        // (the mobile Socials screen prefills from this); stripped for everyone else. Distinct
        // from the manually-entered public Discord social in UserSocials.
        public string? DiscordUsername { get; set; }
        public bool DiscordDmEnabled { get; set; } = true;
    }
}
