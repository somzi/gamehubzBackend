using Template.Common.Models;

namespace Template.DataModels.Extensions
{
    public static class FilterItemExtensions
    {
        public static List<FilterItem> GetConvertedFilterItemList(this List<FilterItem> list)
        {
            return list.OfType<FilterItem>().ToList();
        }
    }
}