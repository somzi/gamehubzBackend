using Template.Common.Enums;

namespace Template.Common.Interfaces
{
    public interface IUnitOfWork
    {
        void SaveChanges(bool setTimestamps = true);

        Task SaveChangesAsync(bool setTimestamps = true);

        IReadOnlyDictionary<BaseEntity, AffectedEntityState> GetAffectedEntities();
    }
}