namespace Template.Logic.Exceptions
{
    public class GoogleTokenParameterException : BaseException
    {
        public GoogleTokenParameterException(ILocalizationService localizationService, string claim)
            : base(localizationService, "Exception.ParameterGoogleToken", claim)
        {
        }
    }
}