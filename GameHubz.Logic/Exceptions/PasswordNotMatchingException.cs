namespace GameHubz.Logic.Exceptions
{
    public class PasswordNotMatchingException : BaseException
    {
        public PasswordNotMatchingException(
            ILocalizationService localizationService)
            : base(localizationService, "Exception.PasswordNotMatching")
        {
        }
    }
}
