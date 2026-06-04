using GameHubz.DataModels.Enums;

namespace GameHubz.DataModels.Models
{
    public class RegisterUserPostDto
    {
        public string FirstName { get; set; } = string.Empty;

        public string LastName { get; set; } = string.Empty;

        public string Email { get; set; } = string.Empty;

        public string Password { get; set; } = string.Empty;

        public Guid UserRoleId { get; set; }

        public RegionType Region { get; set; }

        /// <summary>
        /// Optional ISO 3166-1 alpha-2 country code. When provided, the user's Region is derived
        /// from the country (country dictates region). Null keeps the explicitly chosen Region.
        /// </summary>
        public string? Country { get; set; }

        public string UserName { get; set; } = string.Empty;

        public string Nickname { get; set; } = string.Empty;
    }
}