namespace GameHubz.Logic.Exceptions
{
    public class EmptyEmailException : BaseException
    {
        public EmptyEmailException(
            ILocalizationService localizationService)
            : base(localizationService, "Exception.EmptyEmail")
        {
        }
    }
}
