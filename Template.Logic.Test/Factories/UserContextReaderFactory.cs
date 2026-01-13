using Moq;
using Template.Common.Interfaces;
using Template.Common.Models;
using System.Threading.Tasks;
using Template.DataModels.Consts;

namespace Template.Logic.Test.Factories
{
    internal class UserContextReaderFactory
    {
        internal IUserContextReader CreateService()
        {
            var userContextReader = new Mock<IUserContextReader>();

            userContextReader.Setup(x => x.GetTokenUserInfoFromContext())
                .Returns(new TokenFactory().CreateNullableAsyncToken());

            userContextReader.Setup(x => x.GetTokenUserInfoFromContextThrowIfNull())
                .Returns(new TokenFactory().CreateAsyncToken());

            userContextReader.Setup(x => x.GetRequestData())
                .Returns(Task.FromResult(new UserRequestData(new TokenFactory().CreateToken(), Languages.Serbian)));

            return userContextReader.Object;
        }
    }
}