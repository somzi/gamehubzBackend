using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Template.DataModels.Consts;
using Template.Logic.Tokens;

namespace Template.Logic
{
    public class UserContextReader : IUserContextReader
    {
        private readonly AccessTokenReader accessTokenReader;
        private readonly IHttpContextAccessor httpContextAccessor;
        private readonly ILocalizationService localizationService;

        public UserContextReader(
            AccessTokenReader accessTokenReader,
            IHttpContextAccessor httpContextAccessor,
            ILocalizationService localizationService)
        {
            this.accessTokenReader = accessTokenReader;
            this.httpContextAccessor = httpContextAccessor;
            this.localizationService = localizationService;
        }

        public async Task<TokenUserInfo?> GetTokenUserInfoFromContext()
        {
            if (this.httpContextAccessor?.HttpContext == null)
            {
                return null;
            }

            TokenUserInfo tokenUserInfo = await this.accessTokenReader.ReadFromClaimsPrincipal(
                this.httpContextAccessor.HttpContext.User);

            return await Task.FromResult(tokenUserInfo);
        }

        public async Task<TokenUserInfo> GetTokenUserInfoFromContextThrowIfNull()
        {
            TokenUserInfo? tokenUserInfo = await this.GetTokenUserInfoFromContext();

            if (tokenUserInfo == null)
            {
                throw new UserTokenNotFoundException(this.localizationService);
            }

            return await Task.FromResult(tokenUserInfo!);
        }

        public async Task<UserRequestData> GetRequestData()
        {
            TokenUserInfo tokenUserInfo = await this.GetTokenUserInfoFromContextThrowIfNull();

            string language = this.GetLanguageFromRequest();

            return new UserRequestData(tokenUserInfo, language);
        }

        private string GetLanguageFromRequest()
        {
            return this.GetHeaderValue("Language", defaultValue: Languages.Serbian);
        }

        private string GetHeaderValue(string headerKey, string defaultValue)
        {
            StringValues headerValues = StringValues.Empty;

            bool? headerTryValue = this.httpContextAccessor
                ?.HttpContext
                ?.Request
                .Headers
                .TryGetValue(headerKey, out headerValues);

            if (headerTryValue.HasValue
                && headerTryValue.Value
                && headerValues != StringValues.Empty
                && headerValues.Count > 0)
            {
                return headerValues.First()!;
            }

            return defaultValue;
        }
    }
}