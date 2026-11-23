namespace MapVote.Config
{
    /// <summary>One-way fixes for config files that predate a default change.</summary>
    public static class ConfigMigration
    {
        private static readonly string[] LegacyDefaultSortIds =
            ["most_played", "least_played", "most_liked", "least_liked", "favorites"];

        private static readonly string[] PreviousDefaultSortIds =
            ["alphabetical", "most_positive", "most_negative"];

        /// <summary>
        /// Replaces the old default sort sets with the current list. Custom sort lists are
        /// left untouched. Returns true when the config changed.
        /// </summary>
        public static bool EnsureSortDefaults(FiltersConfig filters)
        {
            List<SortConfig> sorts = filters.Sorts ??= [];
            if (sorts.Count == 0)
            {
                return false;
            }

            bool legacy = sorts.All(static sort => LegacyDefaultSortIds.Contains(sort.Id, StringComparer.Ordinal));
            bool previous = sorts.Count == PreviousDefaultSortIds.Length
                && sorts.All(static sort => PreviousDefaultSortIds.Contains(sort.Id, StringComparer.Ordinal));
            if (!legacy && !previous)
            {
                return false;
            }

            sorts.Clear();
            sorts.AddRange(ConfigDefaults.CreateSorts());
            return true;
        }
    }
}
