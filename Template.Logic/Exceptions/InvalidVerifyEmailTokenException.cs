namespace Template.Logic.Exceptions
{
    public class InvalidVerifyEmailTokenException : BaseException
    {
        public InvalidVerifyEmailTokenException(
            ILocalizationService localizationService)
            : base(localizationService, "Exception.InvalidVerifyEmailToken")
        {
        }
    }
}