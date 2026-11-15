namespace MapVote.Maps
{
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
        public static (List<MapEntry> Maps, int Skipped) ParseLocalMaps(string? response)
        {
            if (string.IsNullOrEmpty(response))
            {
                return ([], 0);
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

            return (maps, skipped);
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
