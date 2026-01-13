namespace Template.Logic.Exceptions
{
    public class EntityNotFoundException : BaseException
    {
        public EntityNotFoundException(
            Guid entityId,
            string entityName,
            ILocalizationService localizationService)
            : base(string.Format(localizationService["Exception.EntityNotFoundExceptionWithDetails"], entityName, entityId))
        {
        }

        public EntityNotFoundException(string searchCriteria, string entityName, ILocalizationService localizationService)
            : base(string.Format(localizationService["Exception.EntityNotFoundExceptionByCriteria"], entityName, searchCriteria))
        {
        }
    }
}