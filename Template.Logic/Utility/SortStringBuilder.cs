namespace Template.Logic.Utility
{
    public class SortStringBuilder : ISortStringBuilder
    {
        public string CreateSortString(IList<SortItem>? sortItemRequests)
        {
            if (sortItemRequests is null || !sortItemRequests.Any())
            {
                return string.Empty;
            }

            var orderBy = string.Empty;

            foreach (var item in sortItemRequests)
            {
                orderBy = $"{orderBy}{item.PropertyName} {item.Direction}, ";
            }

            orderBy = orderBy.Remove(orderBy.Length - 2);

            return orderBy;
        }
    }
}