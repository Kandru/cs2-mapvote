namespace MapVote.Maps
{
    /// <summary>One parsed <c>maps *</c> reply plus the counters used by catalog readiness.</summary>
    public sealed record LocalMapParseResult(List<MapEntry> Maps, int Skipped)
    {
        public static LocalMapParseResult Empty { get; } = new([], 0);

        /// <summary>At least one accepted or explicitly skipped line proves the command answered.</summary>
        public bool Ready => Maps.Count > 0 || Skipped > 0;
    }

    /// <summary>
    /// Pure parsing and merging of the two catalog sources: the <c>maps *</c> reply and
    /// the parsed <c>print_mapgroup_sv</c> reply.
    /// </summary>
    public static class MapCatalogParser
    {
        /// <summary>
        /// Keeps <paramref name="local"/> first, then appends new workshop names.
        /// Names present in both keep their local origin.
        /// </summary>
        public static (List<MapEntry> Maps, int WorkshopSkipped) Merge(
            IReadOnlyList<MapEntry> local,
            IReadOnlyList<MapEntry> workshop)
        {
            List<MapEntry> maps = new(local.Count + workshop.Count);
            HashSet<string> seen = new(StringComparer.Ordinal);
            AppendUnique(maps, seen, local);
            int workshopSkipped = AppendUnique(maps, seen, workshop);
            return (maps, workshopSkipped);
        }

        /// <summary>
        /// Parses a <c>maps *</c> reply into playable local map entries. Unplayable names
        /// are counted as skipped; unrelated output lines are ignored.
        /// </summary>
        public static LocalMapParseResult ParseLocalMaps(string? response)
        {
            if (string.IsNullOrEmpty(response))
            {
                return LocalMapParseResult.Empty;
            }

            List<MapEntry> maps = [];
            HashSet<string> seen = new(StringComparer.Ordinal);
            int skipped = 0;
            foreach (string line in response.Split('\n'))
            {
                MapLineKind kind = MapNameFilter.ClassifyLocalLine(line, out string? name);
                switch (kind)
                {
                    case MapLineKind.Skip:
                        skipped++;
                        break;
                    case MapLineKind.Accept when name is not null && seen.Add(name):
                        maps.Add(new MapEntry(name, MapOrigin.Local));
                        break;
                    case MapLineKind.Accept:
                        skipped++;
                        break;
                }
            }

            return new LocalMapParseResult(maps, skipped);
        }

        private static int AppendUnique(
            List<MapEntry> maps,
            HashSet<string> seen,
            IReadOnlyList<MapEntry> source)
        {
            int skipped = 0;
            foreach (MapEntry entry in source)
            {
                if (seen.Add(entry.Name))
                {
                    maps.Add(entry);
                }
                else
                {
                    skipped++;
                }
            }

            return skipped;
        }
    }
}
