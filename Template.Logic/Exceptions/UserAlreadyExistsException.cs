namespace Template.Logic.Exceptions
{
    public class UserAlreadyExistsException : BaseException
    {
        public UserAlreadyExistsException(
            ILocalizationService localizationService)
            : base(localizationService, "Exception.UserAlreadyExists")
        {
        }
    }
}