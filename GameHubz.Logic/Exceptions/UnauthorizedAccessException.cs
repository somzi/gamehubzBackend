using GameHubz.Logic.Exceptions.Base;

namespace GameHubz.Logic.Exceptions
{
    public class UnauthorizedAccessException : BaseUnauthorizedException
    {
        public UnauthorizedAccessException(ILocalizationService localizationService)
            : base(localizationService, "Exception.UnauthorizedAccessException")
        {
        }
    }
}
