namespace GameHubz.Logic.Exceptions
{
    public class ParameterNullException : ParameterException
    {
        public ParameterNullException(ILocalizationService localizationService, string parameterName)
            : base(localizationService, "Exception.ParameterNullException", parameterName)
        {
        }
    }
}
