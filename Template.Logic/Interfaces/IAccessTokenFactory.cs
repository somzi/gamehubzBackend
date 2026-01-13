using Template.DataModels.Tokens;

namespace Template.Logic.Interfaces
{
    public interface IAccessTokenFactory
    {
        Task<AccessToken> GenerateEncodedToken(TokenUserInfo tokenUserInfo);
    }
}