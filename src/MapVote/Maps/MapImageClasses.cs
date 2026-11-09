namespace MapVote.Maps
{
    /// <summary>
    /// Panorama map-shot class names. Official shots are <c>mv-map-map_{name}</c>;
    /// workshop shots are <c>mv-map-ws_{name}</c>. The name is the map name, so no
    /// generated stem list is required.
    /// </summary>
    public static class MapImageClasses
    {
        public const string Default = "mv-map-default";
        public const string WorkshopPrefix = "mv-map-ws_";
        public const string StockPrefix = "mv-map-map_";

        public static string ClassFor(string? mapName, MapOrigin origin)
        {
            if (!MapNameFilter.TryNormalizePlayableMapId(mapName, out string stem)
                || stem == "default")
            {
                return Default;
            }

            return (origin == MapOrigin.Workshop ? WorkshopPrefix : StockPrefix) + stem;
        }
    }
}
