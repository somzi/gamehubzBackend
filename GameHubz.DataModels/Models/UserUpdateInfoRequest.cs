namespace GameHubz.DataModels.Models
{
    public class UserUpdateInfoRequest
    {
        public string Nickname { get; set; } = string.Empty;
        public Guid UserId { get; set; }
        public string Username { get; set; } = string.Empty;

        /// <summary>
        /// Optional ISO 3166-1 alpha-2 country code. Only applied when the user has no country yet
        /// (set-once then locks). Setting it also derives the user's Region from the country.
        /// </summary>
        public string? Country { get; set; }
    }
}