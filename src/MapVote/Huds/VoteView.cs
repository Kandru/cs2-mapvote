using System.Globalization;
using MapVote.Config;
using MapVote.Players;

namespace MapVote.Huds
{
    /// <summary>Pure decisions of the v2 view, kept out of <see cref="VoteHud"/> so they can be tested.</summary>
    public static class VoteView
    {
        /// <summary>First visible sort that orders names A-Z, or -1 when none is configured.</summary>
        public static int DefaultSortIndex(IReadOnlyList<SortConfig> sorts)
        {
            int count = Math.Min(sorts.Count, VoteIds.FilterSortSlots);
            for (int i = 0; i < count; i++)
            {
                SortConfig sort = sorts[i];
                if (string.Equals(sort.By, "name", StringComparison.OrdinalIgnoreCase)
                    && string.Equals(sort.Order, "asc", StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>Nearest generated countdown span (span-3 … span-90) for the time left.</summary>
        public static int SpanFor(double remainingSeconds) =>
            Math.Clamp((int)Math.Round(remainingSeconds, MidpointRounding.AwayFromZero), VoteIds.SpanMin, VoteIds.SpanMax);

        /// <summary>Whole seconds shown by the countdown text (rounded up, never negative).</summary>
        public static int SecondsLeft(double remainingSeconds) =>
            remainingSeconds <= 0 ? 0 : (int)Math.Ceiling(remainingSeconds);

        public static string FormatTime(int seconds) => $"{seconds / 60}:{seconds % 60:00}";

        /// <summary>
        /// Vote counter for the card badge. Exact with the viewer's thousands separator up to
        /// 99,999 (for example <c>1.000</c> or <c>1,000</c>); above that, compact thousands
        /// and millions keep two counters inside the small pill.
        /// </summary>
        public static string FormatCount(int value, string groupSeparator)
        {
            value = Math.Max(0, value);
            if (value < 100_000)
            {
                string separator = string.IsNullOrEmpty(groupSeparator) ? "," : groupSeparator;
                return value.ToString("#,0", CultureInfo.InvariantCulture)
                    .Replace(",", separator, StringComparison.Ordinal);
            }

            if (value < 1_000_000)
            {
                return (value / 1_000).ToString(CultureInfo.InvariantCulture) + "k";
            }

            double millions = Math.Floor(value / 100_000.0) / 10.0;
            return millions.ToString("0.#", CultureInfo.InvariantCulture) + "M";
        }

        /// <summary>Index into <see cref="VoteIds.ShareClasses"/>: the map's share of all votes in steps of 10 %.</summary>
        public static int ShareStep(int votes, int total) =>
            total <= 0 || votes <= 0
                ? 0
                : Math.Clamp((int)Math.Round(votes * 10.0 / total, MidpointRounding.AwayFromZero), 0, 10);

        /// <summary>
        /// Per-player grid visibility. <paramref name="typeMask"/> has bit t set when the map matches
        /// type chip t (chip 0 = All matches everything).
        /// </summary>
        public static bool IsVisible(int typeMask, int typeChip, bool favoritesOnly, bool isFavorite) =>
            (typeChip <= 0 || (typeMask & (1 << typeChip)) != 0)
            && (!favoritesOnly || isFavorite);

        /// <summary>
        /// How many of a viewer's stored favorites are in the currently displayed map list.
        /// Favorites for maps that left the rotation stay stored but are not counted.
        /// </summary>
        public static int AvailableFavoriteCount(IReadOnlySet<string> favorites, IReadOnlyList<string> maps)
        {
            if (favorites.Count == 0 || maps.Count == 0)
            {
                return 0;
            }

            int count = 0;
            foreach (string map in maps)
            {
                if (favorites.Contains(map))
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>Clicking the selected rating removes it; any other click selects the clicked one.</summary>
        public static PlayerRating NextRating(PlayerRating current, PlayerRating clicked) =>
            current == clicked ? PlayerRating.None : clicked;
    }
}
