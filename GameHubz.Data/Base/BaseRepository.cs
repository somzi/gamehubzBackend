using Microsoft.EntityFrameworkCore;
using GameHubz.Common;
using GameHubz.Common.Interfaces;
using GameHubz.Logic.Interfaces;
using GameHubz.Logic.Utility;

namespace GameHubz.Data
{
    public class BaseRepository
    {
        public BaseRepository(
            DbContext context,
            DateTimeProvider dateTimeProvider,
            IFilterExpressionBuilder filterExpressionBuilder,
            ISortStringBuilder sortStringBuilder,
            ILocalizationService localizationService)
        {
            this.ContextBase = context;
            this.DateTimeProvider = dateTimeProvider;
            this.FilterExpressionBuilder = filterExpressionBuilder;
            this.SortStringBuilder = sortStringBuilder;
            this.LocalizationService = localizationService;
        }

        protected DbContext ContextBase { get; private set; }

        protected DateTimeProvider DateTimeProvider { get; set; }

        protected IFilterExpressionBuilder FilterExpressionBuilder { get; set; }

        protected ISortStringBuilder SortStringBuilder { get; set; }

        protected ILocalizationService LocalizationService { get; }

        public virtual async Task AddEntity(BaseEntity entity, IUserContextReader userContextReader)
        {
            if (entity == null)
            {
                throw new ArgumentNullException(nameof(entity));
            }

            bool isNew = entity.IsNew;

            this.ContextBase.Entry(entity).State = EntityState.Added;

            var token = await userContextReader.GetTokenUserInfoFromContext();

            if (isNew)
            {
                entity.CreatedBy = token?.UserId;
            }

            entity.ModifiedBy = token?.UserId;

            if (entity.Id == null || entity.Id == Guid.Empty)
            {
                entity.Id = Guid.NewGuid();
            }
        }

        public virtual async Task UpdateEntity(BaseEntity entity, IUserContextReader userContextReader)
        {
            var token = await userContextReader.GetTokenUserInfoFromContext();
            entity.ModifiedBy = token?.UserId;

            this.ContextBase.Entry(entity).State = EntityState.Modified;
        }

        public virtual async Task HardDeleteEntity(BaseEntity entity)
        {
            this.ContextBase.Entry(entity).State = EntityState.Deleted;
            await Task.Yield();
        }

        public virtual async Task AddUpdateEntity(BaseEntity entity, IUserContextReader userContextReader)
        {
            if (entity is null)
            {
                throw new ArgumentNullException(nameof(entity));
            }

            if (entity.IsNew)
            {
                await this.AddEntity(entity, userContextReader);
            }
            else
            {
                await this.UpdateEntity(entity, userContextReader);
            }
        }

        public virtual async Task SoftDeleteEntity(BaseEntity entity, IUserContextReader userContextReader)
        {
            if (entity is null)
            {
                throw new ArgumentNullException(nameof(entity));
            }

            entity.IsDeleted = true;
            await this.UpdateEntity(entity, userContextReader);
        }
    }
}
