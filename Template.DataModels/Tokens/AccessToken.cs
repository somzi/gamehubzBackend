namespace Template.DataModels.Tokens
{
    public sealed class AccessToken
    {
        public AccessToken(string token, int expiresIn)
        {
            this.Token = token;
            this.ExpiresIn = expiresIn;
        }

        public string Token { get; }
        public int ExpiresIn { get; }
    }
}