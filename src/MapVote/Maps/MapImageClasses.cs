namespace MapVote.Maps
{
    /// <summary>
    /// Resolves Panorama map-shot CSS classes.
    /// Workshop extracts use <c>mv-map-{stem}</c>; official Valve shots use <c>mv-map-stock-{stem}</c>
    /// so the same map name can exist in both palettes.
    /// </summary>
    public static partial class MapImageClasses
    {
        public const string Default = "mv-map-default";
        public const string WorkshopPrefix = "mv-map-";
        public const string StockPrefix = "mv-map-stock-";

        public static string ClassFor(string? mapName)
        {
            if (string.IsNullOrWhiteSpace(mapName))
            {
                return Default;
            }

            string stem = MapNameFilter.SanitizeCssStem(mapName);
            if (stem == "default")
            {
                return Default;
            }

            // Prefer a workshop extract when both exist (custom art for a Valve map name).
            if (WorkshopStems.Contains(stem))
            {
                return WorkshopPrefix + stem;
            }

            if (StockStems.Contains(stem))
            {
                return StockPrefix + stem;
            }

            // Unknown stem: still try the workshop class name (CSS may have been hand-added).
            return WorkshopPrefix + stem;
        }
    }
}
