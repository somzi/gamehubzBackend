using GameHubz.Common.Consts;
using GameHubz.DataModels.Consts;

namespace GameHubz.Logic.Services
{
    public class AnonymousUserContextReader : IUserContextReader
    {
        public AnonymousUserContextReader()
        {
        }

        public async Task<UserRequestData> GetRequestData()
        {
            TokenUserInfo tokenUserInfo = await this.GetTokenUserInfoFromContextThrowIfNull();

            string language = Languages.Serbian;

            return new UserRequestData(tokenUserInfo, language);
        }

        public Task<TokenUserInfo?> GetTokenUserInfoFromContext()
        {
            TokenUserInfo tokenUserInfo = new()
            {
                UserId = SystemUsers.AppAdminUserId,
                Username = "",
            };

            return Task.FromResult((TokenUserInfo?)tokenUserInfo);
        }

        public Task<TokenUserInfo> GetTokenUserInfoFromContextThrowIfNull()
        {
            return this.GetTokenUserInfoFromContext()!;
        }
    }
}
