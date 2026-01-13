using GameHubz.DataModels.Tokens;

namespace GameHubz.Logic.Interfaces
{
    public interface IAccessTokenFactory
    {
        Task<AccessToken> GenerateEncodedToken(TokenUserInfo tokenUserInfo);
    }
}
