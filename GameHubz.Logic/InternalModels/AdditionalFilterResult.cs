using System.Linq.Expressions;
using GameHubz.Common;

namespace GameHubz.Logic.InternalModels
{
    public class AdditionalFilterResult<TEntity>
        where TEntity : BaseEntity
    {
        public AdditionalFilterResult()
        {
            this.FoundPropertiesButNoResult = false;
            this.Filters = new();
        }

        public bool FoundPropertiesButNoResult { get; set; }

        public List<Expression<Func<TEntity, bool>>> Filters { get; set; }
    }
}
