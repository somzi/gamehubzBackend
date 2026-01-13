using GameHubz.Common.Enums;

namespace GameHubz.Common.Interfaces
{
    public interface IUnitOfWork
    {
        void SaveChanges(bool setTimestamps = true);

        Task SaveChangesAsync(bool setTimestamps = true);

        IReadOnlyDictionary<BaseEntity, AffectedEntityState> GetAffectedEntities();
    }
}
