namespace GameHubz.DataModels.Models
{
    public class UserEdit
    {
        public Guid? Id { get; set; }

        public string FirstName { get; set; } = string.Empty;

        public string LastName { get; set; } = string.Empty;

        public string Email { get; set; } = string.Empty;

        public string Password { get; set; } = string.Empty;

        public string? Language { get; set; }

        public string Username { get; set; } = string.Empty;
        public List<UserHubEdit>? UserHubs { get; set; } = new();

        public List<TournamentRegistrationEdit>? TournamentRegistrations { get; set; } = new();

        public List<MatchEdit>? Matches { get; set; } = new();
        public List<UserSocialEdit>? UserSocials { get; set; } = new();
    }
}