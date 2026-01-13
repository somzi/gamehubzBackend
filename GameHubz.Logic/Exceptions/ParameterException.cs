namespace GameHubz.Logic.Exceptions
{
    public class ParameterException : BaseException
    {
        public ParameterException(ILocalizationService service, string parameterName)
            : this(service, "Exception.ParameterException", parameterName)
        {
        }

        public ParameterException(ILocalizationService service, string key, string parameterName)
            : base(service, key, parameterName)
        {
        }
    }
}
