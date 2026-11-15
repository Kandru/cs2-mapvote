using System.Text.Json;
using CounterStrikeSharp.API.Modules.Extensions;
using MapVote.Config;
using MapVote.Maps;

namespace MapVote.Players
{
    /// <summary>Server-wide map play counts. Memory during the map; disk at map boundaries.</summary>
    public sealed class MapPlayCounts
    {
        private readonly object _gate = new();
        private Dictionary<string, int> _counts = new(StringComparer.Ordinal);
        private bool _dirty;
        private long _revision;

        public static string FilePath(PluginConfig config) =>
            Path.Combine(Path.GetDirectoryName(config.GetConfigPath()) ?? ".", "map_plays.json");

        public void Load(PluginConfig config, Action<string>? warn = null) =>
            LoadFile(FilePath(config), warn);

        internal void LoadFile(string path, Action<string>? warn = null)
        {
            lock (_gate)
            {
                _counts = new Dictionary<string, int>(StringComparer.Ordinal);
                _dirty = false;
                _revision = 0;
            }

            if (!File.Exists(path))
            {
                return;
            }

            try
            {
                Dictionary<string, int>? loaded =
                    JsonSerializer.Deserialize<Dictionary<string, int>>(File.ReadAllBytes(path), PlayerFiles.JsonOptions);
                if (loaded is null)
                {
                    return;
                }

                Dictionary<string, int> counts = new(StringComparer.Ordinal);
                foreach ((string rawName, int count) in loaded)
                {
                    if (count < 0 || !MapNameFilter.TryNormalizePlayableMapId(rawName, out string name))
                    {
                        continue;
                    }

                    counts[name] = counts.TryGetValue(name, out int existing)
                        ? Math.Max(existing, count)
                        : count;
                }

                lock (_gate)
                {
                    _counts = counts;
                }
            }
            catch (Exception ex)
            {
                warn?.Invoke($"failed to load map_plays.json: {ex.Message}");
            }
        }

        public int CountOf(string map)
        {
            if (!MapNameFilter.TryNormalizePlayableMapId(map, out string name))
            {
                return 0;
            }

            lock (_gate)
            {
                return _counts.TryGetValue(name, out int n) ? n : 0;
            }
        }

        public void Increment(string map)
        {
            if (!MapNameFilter.TryNormalizePlayableMapId(map, out string name))
            {
                return;
            }

            lock (_gate)
            {
                _counts.TryGetValue(name, out int n);
                if (n == int.MaxValue)
                {
                    return;
                }

                _counts[name] = n + 1;
                _dirty = true;
                _revision++;
            }
        }

        public void Save(PluginConfig config, Action<string>? warn = null) =>
            SaveFile(FilePath(config), warn);

        internal void SaveFile(string path, Action<string>? warn = null)
        {
            Dictionary<string, int> snapshot;
            long revision;
            lock (_gate)
            {
                if (!_dirty)
                {
                    return;
                }

                snapshot = new Dictionary<string, int>(_counts, StringComparer.Ordinal);
                revision = _revision;
            }

            try
            {
                string? dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                PlayerFiles.WriteBytes(path, JsonSerializer.SerializeToUtf8Bytes(snapshot, PlayerFiles.JsonOptions));
                lock (_gate)
                {
                    // Keep the dirty flag when a newer increment arrived while writing.
                    if (_revision == revision)
                    {
                        _dirty = false;
                    }
                }
            }
            catch (Exception ex)
            {
                warn?.Invoke($"failed to save map_plays.json: {ex.Message}");
            }
        }
    }
}
