using GameHubz.Common;
using GameHubz.DataModels.Extensions;
using GameHubz.DataModels.Interfaces;
using GameHubz.Logic.TestInterfaces;

namespace GameHubz.Logic.Services
{
    public class AppBaseService : BaseService, ITestableService
    {
        internal AppBaseService(IUnitOfWork unitOfWork, IUserContextReader userContextReader, ILocalizationService localizationService)
            : base(unitOfWork)
        {
            this.UserContextReader = userContextReader;
            this.LocalizationService = localizationService;
        }

        internal IAppUnitOfWork AppUnitOfWork => (IAppUnitOfWork)this.UnitOfWork;

        protected internal IUserContextReader UserContextReader { get; }

        protected internal ILocalizationService LocalizationService { get; }

        protected static T? ResolveService<T>(IServiceProvider serviceProvider)
            where T : class
        {
            return serviceProvider.GetService(typeof(T)) as T;
        }

#pragma warning disable CA1822 // Mark members as static

        protected async Task<TEntity> PreserveCreatedData<TEntity>(TEntity entity, IRepository<TEntity> repository)
#pragma warning restore CA1822 // Mark members as static
            where TEntity : BaseEntity
        {
            if (!entity.IsNew)
            {
                TEntity? existing = await repository.GetById(entity.Id!.Value);

                if (existing != null)
                {
                    entity.CreatedOn = existing.CreatedOn;
                    entity.CreatedBy = existing.CreatedBy;
                }
            }

            return entity;
        }

#pragma warning disable CA1822 // Mark members as static

        internal async Task<TEntity> MapToEntity<TEntity, TDto>(TDto dto, IRepository<TEntity> repository, IMapper mapper)
#pragma warning restore CA1822 // Mark members as static
            where TEntity : BaseEntity, new()
            where TDto : IEditableDto
        {
            TEntity? entity;

            if (dto.IsNew())
            {
                entity = new TEntity();
            }
            else
            {
                entity = await repository.GetById(dto.Id!.Value);

                if (entity == null)
                {
                    throw new EntityNotFoundException(dto.Id!.Value, typeof(TEntity).Name, this.LocalizationService);
                }
            }

            mapper.Map(dto, entity!);
            return entity;
        }

        protected async Task UpdateValuesOnEntity<TEntity, TValue>(
            Guid entityId,
            TValue value,
            Action<TEntity, TValue> updateAction,
            IRepository<TEntity> repository)
            where TEntity : BaseEntity
        {
            TEntity? entity = await repository.GetById(entityId);

            if (entity == null)
            {
                throw new EntityNotFoundException(entityId, nameof(TEntity), this.LocalizationService);
            }

            updateAction(entity, value);

            await repository.UpdateEntity(entity, this.UserContextReader);
            await this.SaveAsync();
        }

        protected async Task StageForSoftDelete<TEntity>(
            Guid entityId,
            IRepository<TEntity> repository)
            where TEntity : BaseEntity
        {
            var entity = await repository.ShallowGetById(entityId);

            if (entity is null)
            {
                throw new EntityNotFoundException(entityId, nameof(TEntity), this.LocalizationService);
            }

            await repository.SoftDeleteEntity(entity, this.UserContextReader);
        }

        IAppUnitOfWork ITestableService.AppUnitOfWork => this.AppUnitOfWork;
    }
}
