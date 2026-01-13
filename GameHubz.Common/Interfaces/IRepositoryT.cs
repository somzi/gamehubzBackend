using System.Linq.Expressions;
using GameHubz.Common.Models;

namespace GameHubz.Common.Interfaces
{
    public interface IRepository<TEntity> //: IRepository
        where TEntity : BaseEntity
    {
        Task<IEnumerable<TEntity>> GetAll();

        Task<TEntity?> GetById(Guid id);

        Task<TEntity> GetByIdOrThrowIfNull(Guid id);

        Task<int> CountEntities(IList<FilterItem> filterItems);

        Task<EntityListResult<TEntity>> GetFilteredData(
            IList<FilterItem>? filterItems,
            IList<SortItem>? sortItems,
            int? pageIndex,
            int? pageSize,
            List<Expression<Func<TEntity, bool>>>? additionalFilter = null);

        Task AddEntity(TEntity entity, IUserContextReader userContextReader);

        Task HardDeleteEntity(TEntity entity);

        Task UpdateEntity(TEntity entity, IUserContextReader userContextReader);

        Task SoftDeleteEntity(TEntity entity, IUserContextReader userContextReader);

        Task AddUpdateEntity(TEntity entity, IUserContextReader userContextReader);

        Task<IEnumerable<TEntity>> SelectPagedResults(
          IList<FilterItem> filterItems,
          IList<SortItem> sortItems,
          int pageIndex,
          int pageSize,
          List<Expression<Func<TEntity, bool>>>? additionalFilter = null);

        Task<TEntity?> ShallowGetById(Guid id);

        Task<TEntity> ShallowGetByIdOrThrowIfNull(Guid id);

        Task<bool> AnyExists(Guid id);
    }
}
