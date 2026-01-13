using GameHubz.Logic.Exceptions.Base;

namespace GameHubz.Logic.Exceptions
{
    public class UnauthorizedAccessToServiceException : BaseUnauthorizedException
    {
        public UnauthorizedAccessToServiceException(ILocalizationService localizationService)
            : base(localizationService, "Exception.UnauthorizedAccessToServiceException")
        {
        }
    }
}
