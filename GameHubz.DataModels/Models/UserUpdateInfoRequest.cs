namespace GameHubz.DataModels.Models
{
    public class UserUpdateInfoRequest
    {
        public string Nickname { get; set; } = string.Empty;
        public Guid UserId { get; set; }
        public string Username { get; set; } = string.Empty;
    }
}