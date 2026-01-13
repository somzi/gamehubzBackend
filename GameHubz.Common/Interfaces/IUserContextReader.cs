using GameHubz.Common.Models;

namespace GameHubz.Common.Interfaces
{
    public interface IUserContextReader
    {
        Task<TokenUserInfo?> GetTokenUserInfoFromContext();

        Task<TokenUserInfo> GetTokenUserInfoFromContextThrowIfNull();

        Task<UserRequestData> GetRequestData();
    }
}
