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
        public List<UserHubDto>? UserHubs { get; set; } = new();

        public List<TournamentRegistrationDto>? TournamentRegistrations { get; set; } = new();

        public List<MatchDto>? Matches { get; set; } = new();
    }
}