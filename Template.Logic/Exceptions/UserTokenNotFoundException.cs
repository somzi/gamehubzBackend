namespace Template.Logic.Exceptions
{
    public class UserTokenNotFoundException : BaseException
    {
        public UserTokenNotFoundException(ILocalizationService localizationService)
            : base(localizationService, "Exception.UserTokenNotFoundException")
        {
        }
    }
}