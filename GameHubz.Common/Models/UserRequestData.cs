namespace GameHubz.Common.Models
{
    public class UserRequestData
    {
        public UserRequestData(
            TokenUserInfo tokenUserInfo,
            string language)
        {
            this.TokenUserInfo = tokenUserInfo;
            this.Language = language;
        }

        public TokenUserInfo TokenUserInfo { get; set; }

        public string Language { get; set; }
    }
}
