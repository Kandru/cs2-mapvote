namespace MapVote.Maps
{
    /// <summary>
    /// Display-only map names. The catalog keeps the raw id for config keys,
    /// favorites and play counts; only what the player sees is cleaned up.
    /// </summary>
    public static class MapNameDisplay
    {
        /// <summary>
        /// Drops a leading type token ("de_", "cs_", "fy_", ...) and capitalizes
        /// the first remaining character. Names without a prefix only capitalize.
        /// </summary>
        public static string Format(string map)
        {
            if (string.IsNullOrEmpty(map))
            {
                return map;
            }

            int separator = map.IndexOf('_');
            string name = separator > 0 && separator + 1 < map.Length
                ? map[(separator + 1)..]
                : map;

            return char.ToUpperInvariant(name[0]) + name[1..];
        }

        /// <summary>Alphabetical by the cleaned display name, raw id as tie-break.</summary>
        public static List<MapEntry> OrderByDisplayName(IReadOnlyList<MapEntry> maps) =>
            [.. maps
                .OrderBy(static m => Format(m.Name), StringComparer.Ordinal)
                .ThenBy(static m => m.Name, StringComparer.Ordinal)];
    }
}
