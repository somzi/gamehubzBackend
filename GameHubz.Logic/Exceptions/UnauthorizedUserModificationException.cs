using GameHubz.Logic.Exceptions.Base;

namespace GameHubz.Logic.Exceptions
{
    public class UnauthorizedUserModificationException : BaseUnauthorizedException
    {
        public UnauthorizedUserModificationException(
            ILocalizationService localizationService)
            : base(localizationService, "Exception.UnauthorizedUserModificationException")
        {
        }
    }
}
