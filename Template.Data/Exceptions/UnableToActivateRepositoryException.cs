using Template.Data.Exceptions;

namespace Template.Data.Extensions
{
    public class UnableToActivateRepositoryException : BaseDataException
    {
        public UnableToActivateRepositoryException() : base(Strings.UnableToActivateRepositoryException)
        {
        }
    }
}