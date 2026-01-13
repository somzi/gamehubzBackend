using AutoMapper;
using GameHubz.Common;

namespace GameHubz.Logic.Extensions
{
    internal static class EntityListResultExtension
    {
        public static EntityListDto<TDto> ConvertToDto<TEntity, TDto>(
            this EntityListResult<TEntity> entityListResult,
            IMapper mapper)
            where TEntity : BaseEntity
        {
            var list = mapper.Map<IEnumerable<TDto>>(entityListResult.Items);
            return new EntityListDto<TDto>(list, entityListResult.Count);
        }
    }
}
