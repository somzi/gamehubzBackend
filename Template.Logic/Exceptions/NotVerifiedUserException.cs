namespace Template.Logic.Exceptions
{
    public class NotVerifiedUserException : BaseException
    {
        public NotVerifiedUserException(
            ILocalizationService localizationService)
            : base(localizationService, "Exception.NotVerifiedUser")
        {
        }
    }
}