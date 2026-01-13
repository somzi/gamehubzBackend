namespace Template.Logic.TestInterfaces
{
    public interface ITestableGenericService<TDto, TDtoPost, TDtoEdit>
    {
        Task<EntityListDto<TDto>> GetEntities(
            IList<FilterItem>? filterItems,
            IList<SortItem>? sortItems,
            int? pageIndex,
            int? pageSize);

        Task<TDto> GetEntityById(Guid id);

        Task<TDto> SaveEntity(TDtoPost inputDto, bool doSave = true);

        Task<TDtoEdit> GetEntityEdit(Guid id);

        Task DeleteEntity(Guid id, bool doSave = true);
    }
}