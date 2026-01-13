namespace Template.DataModels.Models
{
    public class EntityListDto<TDto>
    {
        public EntityListDto(IEnumerable<TDto> items, int count)
        {
            this.Items = items;
            this.Count = count;
        }

        public IEnumerable<TDto> Items { get; set; }
        public int Count { get; set; }

        public static EntityListDto<TDto> Empty
        {
            get
            {
                return new EntityListDto<TDto>(new List<TDto>(), 0);
            }
        }
    }
}