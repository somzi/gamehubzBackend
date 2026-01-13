namespace Template.Logic.Exceptions
{
    public class EmptyPasswordException : BaseException
    {
        public EmptyPasswordException(
            ILocalizationService localizationService)
            : base(localizationService, "Exception.EmptyPassword")
        {
        }
    }
}