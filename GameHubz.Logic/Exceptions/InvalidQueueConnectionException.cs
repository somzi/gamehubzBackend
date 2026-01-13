namespace GameHubz.Logic.Exceptions
{
    public class InvalidQueueConnectionException : BaseException
    {
        public InvalidQueueConnectionException(
            ILocalizationService localizationService)
            : base(localizationService, "Exception.InvalidQueueConnection")
        {
        }
    }
}
