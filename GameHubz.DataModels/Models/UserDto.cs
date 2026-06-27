using System.Text.Json.Serialization;
using GameHubz.DataModels.Catalog;
using GameHubz.DataModels.Enums;

namespace GameHubz.DataModels.Models
{
    public class UserDto
    {
        public Guid Id { get; set; }

        public string FirstName { get; set; } = "";

        public string LastName { get; set; } = "";

        public string Email { get; set; } = "";

        public Guid UserRoleId { get; set; }

        public string UserRoleDisplayName { get; set; } = "";

        public string UserRoleSystemName { get; set; } = "";

        public string Language { get; set; } = "";

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

        public List<UserHubDto>? UserHubs { get; set; } = new();

        public List<TournamentRegistrationDto>? TournamentRegistrations { get; set; } = new();

        public List<MatchDto>? Matches { get; set; } = new();

        public List<UserSocialDto>? UserSocials { get; set; } = new();
        public List<TournamentParticipantDto>? TournamentParticipants { get; set; } = new();

        public string? AvatarUrl { get; set; }
    }
}