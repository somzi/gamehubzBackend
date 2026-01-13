namespace Template.Logic.Exceptions
{
    public class InvalidForgotPasswordTokenException : BaseException
    {
        public InvalidForgotPasswordTokenException(
            ILocalizationService localizationService)
            : base(localizationService, "Exception.InvalidForgotPasswordToken")
        {
        }
    }
}