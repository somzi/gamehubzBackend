using Template.Common.Models;

namespace Template.Common.Interfaces
{
    public interface IUserContextReader
    {
        Task<TokenUserInfo?> GetTokenUserInfoFromContext();

        Task<TokenUserInfo> GetTokenUserInfoFromContextThrowIfNull();

        Task<UserRequestData> GetRequestData();
    }
}