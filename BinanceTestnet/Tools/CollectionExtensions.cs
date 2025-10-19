using System;
namespace BinanceTestnet.Tools
{
    public static class CollectionExtensions
    {
        public static decimal SafeAverage<T>(this IEnumerable<T> source, Func<T, decimal> selector)
        {
            if (source == null || !source.Any())
                return 0;
            return source.Average(selector);
        }

        public static IEnumerable<T> SafeWhere<T>(this IEnumerable<T> source, Func<T, bool> predicate)
        {
            return source?.Where(predicate) ?? Enumerable.Empty<T>();
        }

        public static List<T> SafeToList<T>(this IEnumerable<T> source)
        {
            return source?.ToList() ?? new List<T>();
        }
    }
}