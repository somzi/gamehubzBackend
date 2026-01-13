using System.Linq.Expressions;
using Template.Common;
using Template.Common.Interfaces;
using Template.Common.Models;
using Template.Data.Enums;
using Template.Data.Extensions;
using Template.Logic.Interfaces;
using Template.Logic.TestInterfaces;
using Template.Logic.Utility;
using Microsoft.EntityFrameworkCore;
using Template.Data.Exceptions;

namespace Template.Data.Base
{
    public class BaseRepository<TContext, TEntity> : BaseRepository, IRepository<TEntity>, ITestableRepository<TEntity>
         where TContext : DbContext
         where TEntity : BaseEntity
    {
        public BaseRepository(
            TContext context,
            DateTimeProvider dateTimeProvider,
            IFilterExpressionBuilder filterExpressionBuilder,
            ISortStringBuilder sortStringBuilder,
            ILocalizationService localizationService)
            : base(context, dateTimeProvider, filterExpressionBuilder, sortStringBuilder, localizationService)
        {
        }

        public async Task<bool> AnyExists(Guid id)
        {
            return await this.BaseDbSet().AnyAsync(x => x.Id == id);
        }

        public async Task<TEntity?> GetById(Guid id)
        {
            return await this.DbSetForSingle()
                .SingleOrDefaultAsync(e => e.Id == id);
        }

        public async Task<TEntity> GetByIdOrThrowIfNull(Guid id)
        {
            var entity = await this.DbSetForSingle()
                .SingleOrDefaultAsync(e => e.Id == id);

            if (entity == null)
            {
                throw new EntityNotFoundExceptionData(id, typeof(TEntity).Name, this.LocalizationService);
            }

            return entity;
        }

        public async Task<IEnumerable<TEntity>> GetAll()
        {
            return await this.BaseDbSet()
                .ToListAsync();
        }

        public async Task<int> CountEntities(IList<FilterItem> filterItems)
        {
            IFilterCompiled<TEntity> filterCompiled = this.FilterExpressionBuilder.CompileFilter<TEntity>(filterItems);

            return await this.BaseDbSet()
                             .Where(filterCompiled.Expression)
                             .CountAsync();
        }

        Task IRepository<TEntity>.AddUpdateEntity(TEntity entity, IUserContextReader userContextReader)
        {
            return this.AddUpdateEntity(entity, userContextReader);
        }

        Task IRepository<TEntity>.SoftDeleteEntity(TEntity entity, IUserContextReader userContextReader)
        {
            return this.SoftDeleteEntity(entity, userContextReader);
        }

        Task IRepository<TEntity>.AddEntity(TEntity entity, IUserContextReader userContextReader)
        {
            return this.AddEntity(entity, userContextReader);
        }

        Task IRepository<TEntity>.HardDeleteEntity(TEntity entity)
        {
            return this.HardDeleteEntity(entity);
        }

        Task IRepository<TEntity>.UpdateEntity(TEntity entity, IUserContextReader userContextReader)
        {
            return this.UpdateEntity(entity, userContextReader);
        }

        void ITestableRepository<TEntity>.DetachEntity(TEntity entity)
        {
            if (entity == null)
            {
                return;
            }

            this.ContextBase.Entry(entity).State = EntityState.Detached;
        }

        public async Task<TEntity?> ShallowGetById(Guid id)
        {
            return await this.BaseDbSet().Where(x => x.Id == id).SingleOrDefaultAsync();
        }

        public async Task<TEntity> ShallowGetByIdOrThrowIfNull(Guid id)
        {
            TEntity? entity = await this.ShallowGetById(id);

            if (entity == null)
            {
                throw new EntityNotFoundExceptionData(id, typeof(TEntity).Name, this.LocalizationService);
            }

            return entity;
        }

        public async Task<IEnumerable<TEntity>> SelectPagedResults(
           IList<FilterItem> filterItems,
           IList<SortItem> sortItems,
           int pageIndex,
           int pageSize,
           List<Expression<Func<TEntity, bool>>>? additionalFilter = null)
        {
            IQueryable<TEntity> entities = this.GetFilteredItems(filterItems, sortItems, pageIndex, pageSize, additionalFilter);
            return await entities.ToListAsync();
        }

        protected IQueryable<TEntity> GetFilteredItems(
            IList<FilterItem>? filterItems,
            IList<SortItem>? sortItems,
            int pageIndex,
            int pageSize,
            List<Expression<Func<TEntity, bool>>>? additinalFilter = null)
        {
            var query = this.GetFilterItemsQueryable(FilterEntityType.Many, filterItems, additinalFilter);

            var sortString = this.SortStringBuilder.CreateSortString(sortItems);

            return query.OrderByConditional(sortString)
                .Skip(pageIndex * pageSize)
                .Take(pageSize);
        }

        protected Task<int> GetFilteredItemsTotalCount(
            IList<FilterItem>? filterItems,
            List<Expression<Func<TEntity, bool>>>? additinalFilter = null)
        {
            var query = this.GetFilterItemsQueryable(FilterEntityType.Basic, filterItems, additinalFilter);

            return query.CountAsync();
        }

        protected IQueryable<TEntity> GetFilterItemsQueryable(
            FilterEntityType filterEntityType,
            IList<FilterItem>? filterItems,
            List<Expression<Func<TEntity, bool>>>? additinalFilter = null)
        {
            IFilterCompiled<TEntity> filterCompiled = this.FilterExpressionBuilder.CompileFilter<TEntity>(filterItems);

            var query = this.GetQueriableByFilterEntityType(filterEntityType)
                .Where(filterCompiled.Expression);

            if (additinalFilter != null)
            {
                foreach (Expression<Func<TEntity, bool>> filter in additinalFilter)
                {
                    query = query.Where(filter);
                }
            }

            return query;
        }

        protected virtual IQueryable<TEntity> DbSetForSingle()
        {
            return this.DbSetForSingleAndList();
        }

        protected virtual IQueryable<TEntity> DbSetForList()
        {
            return this.DbSetForSingleAndList();
        }

        protected virtual IQueryable<TEntity> DbSetForSingleAndList()
        {
            return this.BaseDbSet();
        }

        protected IQueryable<TEntity> BaseDbSet()
        {
            return this.ContextBase.Set<TEntity>()!.AsNoTracking();
        }

        private IQueryable<TEntity> GetQueriableByFilterEntityType(FilterEntityType filterEntityType)
        {
            return filterEntityType switch
            {
                FilterEntityType.Basic => this.BaseDbSet(),
                FilterEntityType.Many => this.DbSetForList(),
                _ => throw new Exception("Unhandled FilterEntityType"),
            };
        }

        public async Task<EntityListResult<TEntity>> GetFilteredData(
            IList<FilterItem>? filterItems,
            IList<SortItem>? sortItems,
            int? pageIndex,
            int? pageSize,
            List<Expression<Func<TEntity, bool>>>? additinalFilter = null)
        {
            var list = await this.GetFilteredItems(
                filterItems,
                sortItems,
                pageIndex ?? 0,
                pageSize ?? int.MaxValue,
                additinalFilter: additinalFilter).ToListAsync();

            var count = await this.GetFilteredItemsTotalCount(
                filterItems,
                additinalFilter: additinalFilter);

            return new EntityListResult<TEntity>(list, count);
        }

        protected bool IsUnique(TEntity entity, string? value, Func<TEntity, string?> getter)
        {
            if (entity is null)
            {
                throw new ArgumentNullException(nameof(entity));
            }

            if (string.IsNullOrEmpty(value))
            {
                return true;
            }

            if (entity.IsNew)
            {
                Expression<Func<TEntity, bool>> exp = x => getter(x) == value;
                var func = exp.Compile();

                return !this.BaseDbSet()
                    .Any(func);
            }
            else
            {
                Expression<Func<TEntity, bool>> exp = x => x.Id != entity.Id!.Value && getter(x) == value;
                var func = exp.Compile();

                return !this.BaseDbSet()
                    .Any(func);
            }
        }
    }
}