using GameHubz.DataModels.Interfaces;

namespace GameHubz.DataModels.Models
{
    public class UserPost : IEditableDto
    {
        public Guid? Id { get; set; }

        public string FirstName { get; set; } = string.Empty;

        public string LastName { get; set; } = string.Empty;

        public string Email { get; set; } = string.Empty;

        public string Password { get; set; } = string.Empty;

        public Guid UserRoleId { get; set; }

        public string? Language { get; set; }

        public string Username { get; set; } = string.Empty;
    }
}