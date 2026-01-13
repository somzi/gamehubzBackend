namespace Template.Logic.Exceptions
{
    public class InvalidTokenConfigurationException : BaseException
    {
        public InvalidTokenConfigurationException(string configurationKey)
            : this(Strings.InvalidTokenConfigurationException, configurationKey)
        {
        }

        public InvalidTokenConfigurationException(string message, string configurationKey)
            : this(message, configurationKey, null)
        {
        }

        public InvalidTokenConfigurationException(string message, string configurationKey, Exception? innerException)
            : base(message, innerException)
        {
            this.ConfigurationKey = configurationKey;
        }

        public InvalidTokenConfigurationException()
        {
        }

        public InvalidTokenConfigurationException(string message, Exception? innerException)
            : base(message, innerException)
        {
        }

        public string ConfigurationKey
        {
            get;
        } = "";
    }
}