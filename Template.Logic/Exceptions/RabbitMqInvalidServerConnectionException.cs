namespace Template.Logic.Exceptions
{
    public class RabbitMqInvalidServerConnectionException : BaseException
    {
        public RabbitMqInvalidServerConnectionException(
            ILocalizationService localizationService)
            : base(localizationService, "Exception.RabbitMqInvalidServerConnection")
        {
        }
    }
}