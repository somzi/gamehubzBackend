using System.Linq.Expressions;
using Template.Common;
using Template.Logic.Extensions;

namespace Template.Logic.Services
{
    public class SearchService
    {
        private readonly IMapper mapper;

        public SearchService(
            IMapper mapper)
        {
            this.mapper = mapper;
        }

        public async Task<EntityListDto<TDto>> Search<TEntity, TDto>(
            IList<FilterItem>? filterItems,
            IList<SortItem>? sortItems,
            int? pageIndex,
            int? pageSize,
            IRepository<TEntity> repository,
            List<Expression<Func<TEntity, bool>>>? additionalFilter = null)
            where TEntity : BaseEntity
        {
            EntityListResult<TEntity> filteredData =
                await repository
                    .GetFilteredData(
                        filterItems,
                        sortItems,
                        pageIndex,
                        pageSize,
                        additionalFilter: additionalFilter);

            var filteredDataDto = filteredData.ConvertToDto<TEntity, TDto>(this.mapper);

            return filteredDataDto;
        }
    }
}