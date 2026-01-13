using FluentValidation;
using Template.Common;
using Template.DataModels.Interfaces;

namespace Template.Logic.Services
{
    public class ServiceFunctions
    {
        private readonly IMapper mapper;

        private readonly IUserContextReader userContextReader;
        private readonly ILocalizationService localizationService;
        private readonly SearchService searchService;

        public ServiceFunctions(
            IUserContextReader userContextReader,
            ILocalizationService localizationService,
            SearchService searchService,
            IMapper mapper)
        {
            this.mapper = mapper;
            this.userContextReader = userContextReader;
            this.localizationService = localizationService;
            this.searchService = searchService;
        }

        internal async Task<EntityListDto<TDto>> GetEntities<TEntity, TDto>(
            IRepository<TEntity> repository,
            IList<FilterItem>? filterItems,
            IList<SortItem>? sortItems,
            int? pageIndex,
            int? pageSize)
            where TEntity : BaseEntity
        {
            EntityListDto<TDto> searchData = await this.searchService.Search
                <TEntity, TDto>(
                filterItems,
                sortItems,
                pageIndex,
                pageSize,
                repository);

            return searchData;
        }

        internal async Task<TDto> GetEntityById<TEntity, TDto>(IRepository<TEntity> repository, Guid id)
            where TEntity : BaseEntity
        {
            id.ValidateEmptyAndThrow(nameof(id));

            TEntity? data = await repository.GetById(id);

            if (data == null)
            {
                throw new EntityNotFoundException(id, typeof(TDto).Name, this.localizationService);
            }

            TDto model = this.mapper.Map<TDto>(data);

            return model;
        }

        internal async Task<TDtoEdit> GetEntityEdit<TEntity, TDtoEdit>(IRepository<TEntity> repository, Guid id)
            where TEntity : BaseEntity
        {
            id.ValidateEmptyAndThrow(nameof(id));

            TEntity? data = await repository.GetById(id);

            if (data == null)
            {
                throw new EntityNotFoundException(id, typeof(TDtoEdit).Name, this.localizationService);
            }

            TDtoEdit model = this.mapper.Map<TDtoEdit>(data);

            return model;
        }

        internal async Task<TDto> SaveEntity<TEntity, TDto, TDtoPost>(
             IRepository<TEntity> repository,
             AppBaseService service,
             IValidator<TEntity> validator,
             TDtoPost inputDto,
             Func<Guid, Task<TDto>> getById,
             Func<TEntity, TDtoPost, bool, Task>? beforeSave = null,
             Func<TDtoPost, bool, Task>? beforeDtoMapToEntity = null,
             bool doSave = true)
             where TDtoPost : IEditableDto
             where TEntity : BaseEntity, new()
        {
            TEntity entity = await StageEntity(
                repository,
                service,
                validator,
                inputDto,
                beforeSave,
                beforeDtoMapToEntity);

            if (doSave == true)
            {
                await service.SaveAsync();
            }

            return await getById(entity.Id!.Value);
        }

        internal async Task<TEntity> StageEntity<TEntity, TDtoPost>(
            IRepository<TEntity> repository,
            AppBaseService service,
            IValidator<TEntity> validator,
            TDtoPost inputDto,
            Func<TEntity, TDtoPost, bool, Task>? afterStage = null,
            Func<TDtoPost, bool, Task>? beforeDtoMapToEntity = null)
            where TDtoPost : IEditableDto
            where TEntity : BaseEntity, new()
        {
            bool isNew = inputDto.Id.HasValue == false;

            if (beforeDtoMapToEntity != null)
            {
                await beforeDtoMapToEntity(inputDto, isNew);
            }

            TEntity entity = await service.MapToEntity(
                inputDto,
                repository,
                this.mapper);

            validator.ValidateAndThrow(entity);

            await repository.AddUpdateEntity(entity, this.userContextReader);

            if (afterStage != null)
            {
                await afterStage(entity, inputDto, !inputDto.Id.HasValue);
            }

            return entity;
        }

        internal async Task DeleteEntity<TEntity>(
            IRepository<TEntity> repository,
            AppBaseService service,
            Guid id,
            bool doSave = true)
            where TEntity : BaseEntity, new()
        {
            TEntity? entity = await repository.GetById(id);

            if (entity is null)
            {
                throw new EntityNotFoundException(id, typeof(TEntity).Name, this.localizationService);
            }

            await repository.SoftDeleteEntity(entity, this.userContextReader);

            if (doSave == true)
            {
                await service.SaveAsync();
            }
        }
    }
}