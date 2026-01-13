namespace Template.Common.Models
{
    public class EntityListResult<TEntity>
        where TEntity : BaseEntity
    {
        public EntityListResult(IEnumerable<TEntity> items, int count)
        {
            this.Items = items ?? new List<TEntity>();
            this.Count = count;
        }

        public IEnumerable<TEntity> Items { get; set; }

        public int Count { get; set; }
    }
}