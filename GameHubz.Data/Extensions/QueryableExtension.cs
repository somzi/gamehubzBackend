using System.Linq.Dynamic.Core;

namespace GameHubz.Data.Extensions
{
    public static class QueryableExtension
    {
        public static IQueryable<T> OrderByConditional<T>(this IQueryable<T> query, string sortString)
        {
            if (sortString == string.Empty)
            {
                return query;
            }

            return query.OrderBy(sortString);
        }
    }
}
