using Template.Logic.Interfaces;

namespace Template.Data.Exceptions
{
    public class EntityNotFoundExceptionData : BaseDataException
    {
        public EntityNotFoundExceptionData(
            Guid entityId,
            string entityName,
            ILocalizationService localizationService)
            : base(string.Format(localizationService["Exception.EntityNotFoundExceptionWithDetails"], entityName, entityId))
        {
        }

        public EntityNotFoundExceptionData(string searchCriteria, string entityName, ILocalizationService localizationService)
            : base(string.Format(localizationService["Exception.EntityNotFoundExceptionByCriteria"], entityName, searchCriteria))
        {
        }
    }
}