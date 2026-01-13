using Template.Logic.Exceptions.Base;

namespace Template.Logic.Exceptions
{
    public class UnauthorizedAccessException : BaseUnauthorizedException
    {
        public UnauthorizedAccessException(ILocalizationService localizationService)
            : base(localizationService, "Exception.UnauthorizedAccessException")
        {
        }
    }
}