namespace MapVote.Config
{
    /// <summary>One-way fixes for config files that predate a default change.</summary>
    public static class ConfigMigration
    {
        private static readonly string[] LegacyDefaultSortIds =
            ["most_played", "least_played", "most_liked", "least_liked", "favorites"];

        /// <summary>
        /// Replaces exactly the old v2 default sort set with the per-player orders used now.
        /// Custom sort lists are left untouched. Returns true when the config changed.
        /// </summary>
        public static bool EnsureSortDefaults(FiltersConfig filters)
        {
            List<SortConfig> sorts = filters.Sorts ??= [];
            if (sorts.Count == 0
                || sorts.Any(static sort => sort.Id is "alphabetical" or "most_positive" or "most_negative"))
            {
                return false;
            }

            if (!sorts.All(static sort => LegacyDefaultSortIds.Contains(sort.Id, StringComparer.Ordinal)))
            {
                return false;
            }

            sorts.Clear();
            sorts.Add(new SortConfig { Id = "alphabetical", By = "name", Order = "asc" });
            sorts.Add(new SortConfig { Id = "most_positive", By = "rating", Order = "desc" });
            sorts.Add(new SortConfig { Id = "most_negative", By = "rating", Order = "asc" });
            return true;
        }
    }
}
