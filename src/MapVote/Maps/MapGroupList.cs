namespace MapVote.Maps
{
    /// <summary>One <c>print_mapgroup_sv</c> reply split by map origin.</summary>
    public sealed record MapGroupParseResult(
        IReadOnlyList<MapEntry> Local,
        IReadOnlyList<MapEntry> Workshop,
        IReadOnlyDictionary<string, string> WorkshopNamesById,
        int Skipped,
        bool Ready)
    {
        public static MapGroupParseResult Empty { get; } = new(
            [],
            [],
            new Dictionary<string, string>(StringComparer.Ordinal),
            0,
            Ready: false);
    }

    /// <summary>
    /// Parses <c>print_mapgroup_sv</c>. The reply is stateful: a collection uses
    /// <c>Map group: workshop</c> followed by bare workshop names, while an admin mapgroup
    /// uses bare local names and <c>workshop/&lt;id&gt;/&lt;name&gt;</c> paths for workshop entries.
    /// </summary>
    public static class MapGroupList
    {
        public const string WorkshopGroupName = "workshop";

        public static MapGroupParseResult Parse(string? response)
        {
            if (string.IsNullOrWhiteSpace(response))
            {
                return MapGroupParseResult.Empty;
            }

            List<MapEntry> local = [];
            List<MapEntry> workshop = [];
            Dictionary<string, string> workshopNamesById = new(StringComparer.Ordinal);
            HashSet<string> seenLocal = new(StringComparer.Ordinal);
            HashSet<string> seenWorkshop = new(StringComparer.Ordinal);
            int skipped = 0;
            bool workshopGroup = false;
            bool sawMap = false;
            bool sawEmptyList = false;

            void Add(List<MapEntry> maps, HashSet<string> seen, string name, MapOrigin origin)
            {
                if (seen.Add(name))
                {
                    maps.Add(new MapEntry(name, origin));
                }
                else
                {
                    skipped++;
                }
            }

            foreach (string rawLine in response.Split('\n'))
            {
                if (string.IsNullOrWhiteSpace(rawLine))
                {
                    continue;
                }

                string line = rawLine.Trim();
                if (line.Equals("print_mapgroup_sv", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (TryParseHeader(line, out string groupName))
                {
                    workshopGroup = string.Equals(
                        groupName,
                        WorkshopGroupName,
                        StringComparison.OrdinalIgnoreCase);
                    continue;
                }

                if (line.Contains("No maps in mapgroup map list", StringComparison.OrdinalIgnoreCase))
                {
                    sawEmptyList = true;
                    continue;
                }

                // Any other colon line is a header/format line, not a map.
                if (line.Contains(':'))
                {
                    continue;
                }

                if (MapNameFilter.TryParseWorkshopMapPath(line, out string name, out string workshopId))
                {
                    sawMap = true;
                    workshopNamesById.TryAdd(workshopId, name);
                    Add(workshop, seenWorkshop, name, MapOrigin.Workshop);
                    continue;
                }

                if (!MapNameFilter.TryNormalizePlayableMapId(line, out name))
                {
                    skipped++;
                    continue;
                }

                sawMap = true;
                if (workshopGroup)
                {
                    Add(workshop, seenWorkshop, name, MapOrigin.Workshop);
                }
                else
                {
                    Add(local, seenLocal, name, MapOrigin.Local);
                }
            }

            return new MapGroupParseResult(
                local,
                workshop,
                workshopNamesById,
                skipped,
                Ready: sawMap || sawEmptyList);
        }

        public static bool TryParseHeader(string line, out string groupName)
        {
            const string prefix = "Map group:";
            groupName = string.Empty;
            if (!line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            groupName = line[prefix.Length..].Trim();
            return true;
        }
    }
}
