using System;
using System.Threading.Tasks;
using Template.Common.Models;

namespace Template.Logic.Test.Factories
{
    internal class TokenFactory
    {
        internal Task<TokenUserInfo?> CreateNullableAsyncToken()
        {
            var tokenUserInfo = MockTokenUserInfo();

            return Task.FromResult(tokenUserInfo)!;
        }

        internal Task<TokenUserInfo> CreateAsyncToken()
        {
            var tokenUserInfo = MockTokenUserInfo();

            return Task.FromResult(tokenUserInfo);
        }

        internal TokenUserInfo CreateToken()
        {
            return MockTokenUserInfo();
        }

        private static TokenUserInfo MockTokenUserInfo()
        {
            var tokenUserInfo = new TokenUserInfo
            {
                Role = "Admin",
                UserId = Guid.Parse(Template.Logic.Test.Consts.TestUserId),
            };

            return tokenUserInfo;
        }
    }
}