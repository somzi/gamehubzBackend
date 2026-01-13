using Template.Logic.Exceptions.Base;

namespace Template.Logic.Exceptions
{
    public class UnauthorizedAccessToServiceException : BaseUnauthorizedException
    {
        public UnauthorizedAccessToServiceException(ILocalizationService localizationService)
            : base(localizationService, "Exception.UnauthorizedAccessToServiceException")
        {
        }
    }
}