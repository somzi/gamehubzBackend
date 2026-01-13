using FluentValidation;
using Template.Common;
using Template.DataModels.Interfaces;
using Template.Logic.TestInterfaces;

namespace Template.Logic.Services
{
    public abstract class AppBaseServiceGeneric<TEntity, TDto, TDtoPost, TDtoEdit>
        : AppBaseService, ITestableGenericService<TDto, TDtoPost, TDtoEdit>
        where TEntity : BaseEntity, new()
        where TDtoPost : IEditableDto
    {
        protected readonly ServiceFunctions ServiceFunctions;

        public AppBaseServiceGeneric(
            IUnitOfWork unitOfWork,
            IUserContextReader userContextReader,
            ILocalizationService localizationService,
            SearchService searchService,
            IValidator<TEntity> validator,
            IMapper mapper,
            ServiceFunctions serviceFunctions)
            : base(unitOfWork, userContextReader, localizationService)
        {
            this.Mapper = mapper;
            this.Validator = validator;
            this.SearchService = searchService;
            this.ServiceFunctions = serviceFunctions;
        }

        protected IMapper Mapper { get; private set; }
        protected IValidator<TEntity> Validator { get; private set; }
        protected SearchService SearchService { get; private set; }

        public virtual async Task<EntityListDto<TDto>> GetEntities(
            IList<FilterItem>? filterItems,
            IList<SortItem>? sortItems,
            int? pageIndex,
            int? pageSize)
        {
            EntityListDto<TDto> searchData = await this.ServiceFunctions.GetEntities<TEntity, TDto>(
                this.GetRepository(), filterItems, sortItems, pageIndex, pageSize);

            return searchData;
        }

        public virtual async Task<TDto> GetEntityById(Guid id)
        {
            TDto model = await this.ServiceFunctions.GetEntityById<TEntity, TDto>(this.GetRepository(), id);

            return model;
        }

        public virtual async Task<TDtoEdit> GetEntityEdit(Guid id)
        {
            TDtoEdit model = await this.ServiceFunctions.GetEntityEdit<TEntity, TDtoEdit>(this.GetRepository(), id);

            return model;
        }

        public virtual async Task<TDto> SaveEntity(TDtoPost inputDto, bool doSave = true)
        {
            TDto model = await this.ServiceFunctions.SaveEntity(
                this.GetRepository(),
                this,
                this.Validator,
                inputDto,
                this.GetEntityById,
                this.BeforeSave,
                this.BeforeDtoMapToEntity);

            return model;
        }

        public virtual async Task DeleteEntity(
            Guid id,
            bool doSave = true)
        {
            await this.BeforeDelete(id);
            await this.ServiceFunctions.DeleteEntity(this.GetRepository(), this, id, doSave);
        }

        protected abstract IRepository<TEntity> GetRepository();

        protected virtual async Task BeforeSave(TEntity entity, TDtoPost inputDto, bool isNew)
        {
            await Task.CompletedTask;
        }

        protected virtual async Task BeforeDtoMapToEntity(TDtoPost inputDto, bool isNew)
        {
            await Task.CompletedTask;
        }

        protected virtual async Task BeforeDelete(Guid entityId)
        {
            await Task.CompletedTask;
        }
    }
}