namespace MapVote.Maps
{
    /// <summary>
    /// Display-only map names. The catalog keeps the raw id for config keys,
    /// favorites and play counts; only what the player sees is cleaned up.
    /// </summary>
    public static class MapNameDisplay
    {
        /// <summary>
        /// Longest leading token still treated as a map type. Covers "de_",
        /// "cs_", "fy_", "aim_", "rush_", "surf_" and similar. A longer token
        /// belongs to the name ("mymapname_1" is a name, not a type).
        /// </summary>
        private const int MaxTypePrefixLength = 4;

        /// <summary>
        /// Drops a leading type token of at most <see cref="MaxTypePrefixLength"/>
        /// characters ("de_", "cs_", "rush_", ...), turns the remaining underscores
        /// into spaces and capitalizes each word. A longer leading token is part of
        /// the name and is kept; names without a prefix are only word-separated
        /// and capitalized.
        /// </summary>
        public static string Format(string map)
        {
            if (string.IsNullOrEmpty(map))
            {
                return map;
            }

            int separator = map.IndexOf('_');
            string name = separator > 0
                && separator <= MaxTypePrefixLength
                && separator + 1 < map.Length
                    ? map[(separator + 1)..]
                    : map;

            // Single-word names (the common case) need no splitting at all.
            if (!name.Contains('_'))
            {
                return char.ToUpperInvariant(name[0]) + name[1..];
            }

            string[] words = name.Split('_', StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < words.Length; i++)
            {
                words[i] = char.ToUpperInvariant(words[i][0]) + words[i][1..];
            }

            return string.Join(' ', words);
        }

        /// <summary>
        /// Wraps a name in non-breaking spaces for dialog variables: the client
        /// translates a value that equals one of its tokens ("Overpass"), and the
        /// padding looks the same but no longer matches.
        /// </summary>
        public static string ForHud(string text) =>
            text.Length == 0 ? text : $"{NonBreakingSpace}{text}{NonBreakingSpace}";

        public const char NonBreakingSpace = (char)0xA0;
    }
}
