using MapVote.Config;

namespace MapVote.Maps
{
    /// <summary>
    /// Per-map settings in config <c>maps</c>.
    /// Keys: <c>map_{name}</c> (local) / <c>ws_{name}</c> (workshop). Missing key = shown.
    /// </summary>
    public static class MapVisibility
    {
        public static string KeyFor(MapEntry entry) => KeyFor(entry.Name, entry.Origin);

        public static string KeyFor(string name, MapOrigin origin) =>
            origin == MapOrigin.Workshop ? $"ws_{name}" : $"map_{name}";

        /// <summary>
        /// Merge discovered maps into <paramref name="maps"/>: keep every existing setting,
        /// add missing discovered keys as enabled with <paramref name="nowUnix"/>, and backfill
        /// a missing timestamp without touching an existing one. With <paramref name="prune"/>
        /// (a fully ready catalog), keys that are no longer discovered are removed.
        /// Returns whether the dictionary changed (keys, values, or order).
        /// </summary>
        public static bool TrySync(
            Dictionary<string, MapSettings> maps,
            IReadOnlyList<MapEntry> discovered,
            long nowUnix,
            bool prune)
        {
            bool changed = false;
            HashSet<string> discoveredKeys = new(discovered.Count, StringComparer.Ordinal);
            foreach (MapEntry entry in discovered)
            {
                string key = KeyFor(entry);
                if (!discoveredKeys.Add(key))
                {
                    continue;
                }

                if (!maps.TryGetValue(key, out MapSettings? setting) || setting is null)
                {
                    maps[key] = new MapSettings { Enabled = true, Added = nowUnix };
                    changed = true;
                    continue;
                }

                if (setting.Added <= 0)
                {
                    setting.Added = nowUnix;
                    changed = true;
                }
            }

            // An empty partial reply must never wipe the recorded history.
            if (prune && discoveredKeys.Count > 0)
            {
                List<string> stale = [.. maps.Keys.Where(key => !discoveredKeys.Contains(key))];
                foreach (string key in stale)
                {
                    maps.Remove(key);
                }

                changed |= stale.Count > 0;
            }

            if (IsOrdinalSorted(maps))
            {
                // New keys already sit in sorted position, or nothing changed.
                return changed;
            }

            List<KeyValuePair<string, MapSettings>> sorted =
                [.. maps.OrderBy(static kv => kv.Key, StringComparer.Ordinal)];
            maps.Clear();
            foreach ((string key, MapSettings value) in sorted)
            {
                maps[key] = value;
            }

            return true;
        }

        /// <summary>Timestamp recorded for a map, or 0 when unknown.</summary>
        public static long AddedFor(
            string name,
            MapOrigin origin,
            IReadOnlyDictionary<string, MapSettings> maps) =>
            maps.TryGetValue(KeyFor(name, origin), out MapSettings? setting) && setting is not null
                ? setting.Added
                : 0;

        /// <summary>
        /// Catalog order, dropping maps whose config entry is explicitly disabled.
        /// When <paramref name="excludeName"/> is set, that map name is skipped too.
        /// </summary>
        public static List<MapEntry> Shown(
            IReadOnlyList<MapEntry> catalog,
            IReadOnlyDictionary<string, MapSettings> maps,
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

                if (maps.TryGetValue(KeyFor(entry), out MapSettings? setting) && setting is { Enabled: false })
                {
                    continue;
                }

                result.Add(entry);
            }

            return result;
        }

        /// <summary>True if at least one catalog map is enabled (missing key = shown).</summary>
        public static bool AnyShown(
            IReadOnlyList<MapEntry> catalog,
            IReadOnlyDictionary<string, MapSettings> maps)
        {
            foreach (MapEntry entry in catalog)
            {
                if (!maps.TryGetValue(KeyFor(entry), out MapSettings? setting) || setting is not { Enabled: false })
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsOrdinalSorted(Dictionary<string, MapSettings> maps)
        {
            string? prev = null;
            foreach (string key in maps.Keys)
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
