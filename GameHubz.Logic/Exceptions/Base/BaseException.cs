namespace GameHubz.Logic.Exceptions
{
    public class BaseException : Exception
    {
        protected BaseException()
        {
        }

        protected BaseException(string message)
            : base(message)
        {
        }

        protected BaseException(string message, Exception? innerException)
            : base(message, innerException)
        {
        }

        public BaseException(ILocalizationService localizationService, string key, params string[] args)
            : this(string.Format(localizationService[key], args))
        {
        }
    }
}
