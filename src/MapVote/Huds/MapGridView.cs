using MapVote.Config;
using MapVote.Maps;

namespace MapVote.Huds
{
    /// <summary>Immutable per-map display data shared by every viewer.</summary>
    public sealed record MapGridEntry(
        string Name,
        string DisplayName,
        MapOrigin Origin,
        long AddedUnix);

    /// <summary>
    /// Shared snapshot of the map list in base order. Built once per bind from the catalog
    /// settings; per-player state only decides order and visibility, never map data.
    /// </summary>
    public sealed class MapGridView
    {
        private readonly Dictionary<string, MapGridEntry> _byName;

        private MapGridView(List<MapGridEntry> items)
        {
            Items = items;
            _byName = new Dictionary<string, MapGridEntry>(items.Count, StringComparer.Ordinal);
            foreach (MapGridEntry item in items)
            {
                _byName[item.Name] = item;
            }
        }

        /// <summary>Entries in base (display) order.</summary>
        public IReadOnlyList<MapGridEntry> Items { get; }

        public static MapGridView Build(
            IReadOnlyList<string> orderedNames,
            IReadOnlyDictionary<string, MapOrigin> origins,
            IReadOnlyDictionary<string, MapSettings> settings,
            Func<string, string> formatName)
        {
            List<MapGridEntry> items = new(orderedNames.Count);
            foreach (string name in orderedNames)
            {
                MapOrigin origin = origins.GetValueOrDefault(name, MapOrigin.Local);
                items.Add(new MapGridEntry(
                    name,
                    MapNameDisplay.ForHud(formatName(name)),
                    origin,
                    MapVisibility.AddedFor(name, origin, settings)));
            }

            return new MapGridView(items);
        }

        public MapGridEntry? Find(string name) =>
            _byName.TryGetValue(name, out MapGridEntry? entry) ? entry : null;
    }
}
