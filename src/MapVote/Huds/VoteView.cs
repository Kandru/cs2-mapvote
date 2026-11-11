using MapVote.Players;

namespace MapVote.Huds
{
    /// <summary>Pure decisions of the v2 view, kept out of <see cref="VoteHud"/> so they can be tested.</summary>
    public static class VoteView
    {
        /// <summary>Nearest generated countdown span (span-3 … span-90) for the time left.</summary>
        public static int SpanFor(double remainingSeconds) =>
            Math.Clamp((int)Math.Round(remainingSeconds, MidpointRounding.AwayFromZero), VoteIds.SpanMin, VoteIds.SpanMax);

        /// <summary>Whole seconds shown by the countdown text (rounded up, never negative).</summary>
        public static int SecondsLeft(double remainingSeconds) =>
            remainingSeconds <= 0 ? 0 : (int)Math.Ceiling(remainingSeconds);

        public static string FormatTime(int seconds) => $"{seconds / 60}:{seconds % 60:00}";

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

        /// <summary>Clicking the selected rating removes it; any other click selects the clicked one.</summary>
        public static PlayerRating NextRating(PlayerRating current, PlayerRating clicked) =>
            current == clicked ? PlayerRating.None : clicked;
    }
}
