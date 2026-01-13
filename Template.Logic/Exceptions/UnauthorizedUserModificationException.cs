using Template.Logic.Exceptions.Base;

namespace Template.Logic.Exceptions
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