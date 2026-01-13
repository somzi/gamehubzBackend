using GameHubz.Data.Exceptions;

namespace GameHubz.Data.Extensions
{
    public class UnableToActivateRepositoryException : BaseDataException
    {
        public UnableToActivateRepositoryException() : base(Strings.UnableToActivateRepositoryException)
        {
        }
    }
}
