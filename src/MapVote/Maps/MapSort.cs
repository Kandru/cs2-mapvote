using MapVote.Config;

namespace MapVote.Maps
{
    /// <summary>Applies configured sort chips to a map list.</summary>
    public static class MapSort
    {
        public static bool ChipEnabled(SortConfig sort, IReadOnlySet<string> favorites) =>
            !sort.Only || favorites.Count > 0;

        public static List<string> Apply(
            IReadOnlyList<string> maps,
            IReadOnlyList<string> catalogOrder,
            SortConfig sort,
            Func<string, int> playCount,
            IReadOnlySet<string> favorites)
        {
            IEnumerable<string> query = sort.Only ? maps.Where(favorites.Contains) : maps;
            bool descending = !string.Equals(sort.Order, "asc", StringComparison.OrdinalIgnoreCase);
            string by = sort.By.Trim().ToLowerInvariant();

            // Unknown `by` keeps catalog order; known keys use catalog index as a stable tie-break.
            return by switch
            {
                "plays" => OrderBy(query, playCount, descending)
                    .ThenBy(m => CatalogIndex(catalogOrder, m))
                    .ToList(),
                "favorite" => OrderBy(query, m => favorites.Contains(m) ? 1 : 0, descending)
                    .ThenBy(m => CatalogIndex(catalogOrder, m))
                    .ToList(),
                "name" => OrderBy(query, m => m, descending, StringComparer.Ordinal)
                    .ThenBy(m => CatalogIndex(catalogOrder, m))
                    .ToList(),
                _ => query.OrderBy(m => CatalogIndex(catalogOrder, m)).ToList(),
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
