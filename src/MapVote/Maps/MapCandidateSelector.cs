namespace MapVote.Maps
{
    /// <summary>
    /// Pure map-candidate selection shared by the end-match vote and the !maps browser.
    /// Keeps visibility, ordering and priority rules in one place instead of the plugin partial.
    /// </summary>
    public static class MapCandidateSelector
    {
        /// <summary>
        /// Visible catalog maps, optionally excluding the current map, capped at
        /// <paramref name="limit"/>. Priority names (nominations) survive the cap.
        /// </summary>
        public static List<MapEntry> Select(
            IReadOnlyList<MapEntry> catalog,
            IReadOnlyDictionary<string, bool> votable,
            string? excludeName,
            int limit,
            IReadOnlyList<string>? priority = null,
            Action<int, int>? onTruncated = null)
        {
            if (catalog.Count == 0)
            {
                return [];
            }

            List<MapEntry> maps = MapVisibility.Shown(catalog, votable, excludeName);
            if (maps.Count > limit)
            {
                onTruncated?.Invoke(maps.Count, limit);
                maps = TakeInBaseOrder(maps, priority, limit);
            }

            return maps;
        }

        /// <summary>
        /// Keeps priority maps first, then the start of the stable base order. The source list
        /// keeps catalog order; the HUD applies the same base order to every viewer.
        /// </summary>
        public static List<MapEntry> TakeInBaseOrder(
            List<MapEntry> maps,
            IReadOnlyList<string>? priority,
            int limit)
        {
            if (maps.Count <= limit)
            {
                return maps;
            }

            HashSet<string> available = new(maps.ConvertAll(static map => map.Name), StringComparer.Ordinal);
            HashSet<string> keep = new(StringComparer.Ordinal);
            if (priority is { Count: > 0 })
            {
                foreach (string name in priority)
                {
                    if (keep.Count >= limit)
                    {
                        break;
                    }

                    if (available.Contains(name))
                    {
                        keep.Add(name);
                    }
                }
            }

            if (keep.Count < limit)
            {
                foreach (string name in MapOrder.Sort([.. available], MapNameDisplay.Format))
                {
                    if (keep.Count >= limit)
                    {
                        break;
                    }

                    keep.Add(name);
                }
            }

            return maps.FindAll(map => keep.Contains(map.Name));
        }

        /// <summary>Drops empty names and duplicate catalog entries while preserving order.</summary>
        public static List<MapEntry> Distinct(IReadOnlyList<MapEntry> maps)
        {
            HashSet<string> seen = new(StringComparer.Ordinal);
            List<MapEntry> unique = new(maps.Count);
            foreach (MapEntry map in maps)
            {
                if (!string.IsNullOrEmpty(map.Name) && seen.Add(map.Name))
                {
                    unique.Add(map);
                }
            }

            return unique;
        }
    }
}
