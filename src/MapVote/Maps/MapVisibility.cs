namespace MapVote.Maps
{
    /// <summary>
    /// Per-map vote enable flags in config <c>maps_votable</c>.
    /// Keys: <c>map_{name}</c> (local) / <c>ws_{name}</c> (workshop). Missing = shown.
    /// </summary>
    public static class MapVisibility
    {
        public static string KeyFor(MapEntry entry) =>
            entry.Origin == MapOrigin.Workshop ? $"ws_{entry.Name}" : $"map_{entry.Name}";

        /// <summary>
        /// Merge discovered maps into <paramref name="votable"/>: keep every existing entry,
        /// add missing discovered keys as <c>true</c>, sort keys ordinal. Returns whether
        /// the dictionary changed (keys, values, or order).
        /// </summary>
        public static bool TrySync(
            Dictionary<string, bool> votable,
            IReadOnlyList<MapEntry> discovered)
        {
            bool added = false;
            foreach (MapEntry entry in discovered)
            {
                added |= votable.TryAdd(KeyFor(entry), true);
            }

            if (IsOrdinalSorted(votable))
            {
                // New keys already sit in sorted position, or nothing changed.
                return added;
            }

            List<KeyValuePair<string, bool>> sorted =
                [.. votable.OrderBy(static kv => kv.Key, StringComparer.Ordinal)];
            votable.Clear();
            foreach ((string key, bool value) in sorted)
            {
                votable[key] = value;
            }

            return true;
        }

        /// <summary>
        /// Catalog order, dropping maps whose config key is explicitly <c>false</c>.
        /// When <paramref name="excludeName"/> is set, that map name is skipped too.
        /// </summary>
        public static List<MapEntry> Shown(
            IReadOnlyList<MapEntry> catalog,
            IReadOnlyDictionary<string, bool> votable,
            string? excludeName = null)
        {
            List<MapEntry> result = new(catalog.Count);
            foreach (MapEntry entry in catalog)
            {
                if (excludeName is not null
                    && string.Equals(entry.Name, excludeName, StringComparison.Ordinal))
                {
                    continue;
                }

                if (votable.TryGetValue(KeyFor(entry), out bool enabled) && !enabled)
                {
                    continue;
                }

                result.Add(entry);
            }

            return result;
        }

        /// <summary>True if at least one catalog map is votable (missing key = shown).</summary>
        public static bool AnyShown(
            IReadOnlyList<MapEntry> catalog,
            IReadOnlyDictionary<string, bool> votable)
        {
            foreach (MapEntry entry in catalog)
            {
                if (!votable.TryGetValue(KeyFor(entry), out bool enabled) || enabled)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsOrdinalSorted(Dictionary<string, bool> votable)
        {
            string? prev = null;
            foreach (string key in votable.Keys)
            {
                if (prev is not null && StringComparer.Ordinal.Compare(prev, key) > 0)
                {
                    return false;
                }

                prev = key;
            }

            return true;
        }
    }
}
