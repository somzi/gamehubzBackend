namespace Template.Common.Extensions
{
    public static class CollectionExtensions
    {
        public static void ForEach<T>(this IList<T> list, Action<T> action)
        {
            foreach (T t in list)
            {
                action(t);
            }
        }

        public static void ForEach<T>(this IEnumerable<T> list, Action<T> action)
        {
            foreach (T t in list)
            {
                action(t);
            }
        }
    }
}