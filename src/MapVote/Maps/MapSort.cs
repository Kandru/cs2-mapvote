using MapVote.Config;

namespace MapVote.Maps
{
    /// <summary>Applies a configured sort chip to a map list. Sorting never removes maps.</summary>
    public static class MapSort
    {
        public static List<string> Apply(
            IReadOnlyList<string> maps,
            IReadOnlyList<string> catalogOrder,
            SortConfig sort,
            Func<string, int> playCount,
            Func<string, int> ratingScore,
            Func<string, string>? nameKey = null,
            IReadOnlySet<string>? favorites = null)
        {
            bool descending = !string.Equals(sort.Order, "asc", StringComparison.OrdinalIgnoreCase);
            string by = (sort.By ?? string.Empty).Trim().ToLowerInvariant();

            // Unknown `by` keeps catalog order; known keys use catalog index as a stable tie-break.
            return by switch
            {
                "plays" => OrderBy(maps, playCount, descending)
                    .ThenBy(m => CatalogIndex(catalogOrder, m))
                    .ToList(),
                "rating" => OrderBy(maps, ratingScore, descending)
                    .ThenBy(m => CatalogIndex(catalogOrder, m))
                    .ToList(),
                "name" => OrderBy(maps, nameKey ?? (static m => m), descending, StringComparer.Ordinal)
                    .ThenBy(m => CatalogIndex(catalogOrder, m))
                    .ToList(),
                "favorite" => OrderBy(maps, m => favorites?.Contains(m) == true ? 1 : 0, descending)
                    .ThenBy(m => CatalogIndex(catalogOrder, m))
                    .ToList(),
                _ => maps.OrderBy(m => CatalogIndex(catalogOrder, m)).ToList(),
            };
        }

        private static IOrderedEnumerable<string> OrderBy<TKey>(
            IEnumerable<string> maps,
            Func<string, TKey> key,
            bool descending,
            IComparer<TKey>? comparer = null) =>
            descending
                ? maps.OrderByDescending(key, comparer)
                : maps.OrderBy(key, comparer);

        private static int CatalogIndex(IReadOnlyList<string> catalogOrder, string map)
        {
            for (int i = 0; i < catalogOrder.Count; i++)
            {
                if (catalogOrder[i] == map)
                {
                    return i;
                }
            }

            return int.MaxValue;
        }
    }
}
