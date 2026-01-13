using GameHubz.Logic.Exceptions.Base;

namespace GameHubz.Logic.Exceptions
{
    public class UnauthorizedAccessToObjectException : BaseUnauthorizedException
    {
        public UnauthorizedAccessToObjectException(
            ILocalizationService localizationService,
            string objectToAccess,
            string objectId)
            : base(localizationService,
                  "Exception.UnauthorizedAccessToObjectException",
                  objectToAccess,
                  objectId)
        {
        }
    }
}
