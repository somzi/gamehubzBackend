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

        public string UserName { get; set; } = string.Empty;

        public string Nickname { get; set; } = string.Empty;
    }
}