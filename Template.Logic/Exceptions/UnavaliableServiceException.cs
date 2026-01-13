namespace Template.Logic.Exceptions
{
    public class UnavaliableServiceException : BaseException
    {
        public UnavaliableServiceException(
            ILocalizationService localizationService)
            : base(localizationService, "Exception.UnavaliableService")
        {
        }
    }
}