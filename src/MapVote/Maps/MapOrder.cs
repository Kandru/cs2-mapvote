namespace MapVote.Maps
{
    /// <summary>
    /// Stable base order for the map grid. Shuffle assigns a random rank per map once per
    /// match; later calls (browse, vote, reopen) reuse it, so the list does not jump around.
    /// </summary>
    public static class MapOrder
    {
        public const string Shuffle = "shuffle";
        public const string Alphabetical = "alphabetical";

        private static readonly Dictionary<string, int> Rank = new(StringComparer.Ordinal);
        private static string _mode = Shuffle;

        public static void Configure(string? order)
        {
            string mode = IsAlphabetical(order) ? Alphabetical : Shuffle;
            if (mode == _mode)
            {
                return;
            }

            _mode = mode;
            Rank.Clear();
        }

        /// <summary>New match: keep the mode, forget the ranks so the next list shuffles again.</summary>
        public static void ResetMatch() => Rank.Clear();

        public static List<string> Sort(IReadOnlyList<string> maps, Func<string, string> nameKey)
        {
            if (_mode == Alphabetical)
            {
                return [.. maps
                    .OrderBy(nameKey, StringComparer.Ordinal)
                    .ThenBy(static map => map, StringComparer.Ordinal)];
            }

            foreach (string map in maps)
            {
                if (!Rank.ContainsKey(map))
                {
                    Rank[map] = Random.Shared.Next();
                }
            }

            return [.. maps
                .OrderBy(map => Rank[map])
                .ThenBy(nameKey, StringComparer.Ordinal)
                .ThenBy(static map => map, StringComparer.Ordinal)];
        }

        private static bool IsAlphabetical(string? order) =>
            string.Equals(order, Alphabetical, StringComparison.OrdinalIgnoreCase);
    }
}
