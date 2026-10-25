using System.Text.Json;
using CounterStrikeSharp.API.Modules.Extensions;
using MapVote.Config;

namespace MapVote.Players
{
    /// <summary>Server-wide map play counts. Memory during the map; disk at map boundaries.</summary>
    public sealed class MapPlayCounts
    {
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        private readonly object _gate = new();
        private Dictionary<string, int> _counts = new(StringComparer.Ordinal);
        private bool _dirty;

        public static string FilePath(PluginConfig config) =>
            Path.Combine(Path.GetDirectoryName(config.GetConfigPath()) ?? ".", "map_plays.json");

        public void Load(PluginConfig config, Action<string>? warn = null)
        {
            lock (_gate)
            {
                _counts = new Dictionary<string, int>(StringComparer.Ordinal);
                _dirty = false;
            }

            string path = FilePath(config);
            if (!File.Exists(path))
            {
                return;
            }

            try
            {
                Dictionary<string, int>? loaded =
                    JsonSerializer.Deserialize<Dictionary<string, int>>(File.ReadAllBytes(path), JsonOptions);
                if (loaded is null)
                {
                    return;
                }

                lock (_gate)
                {
                    _counts = new Dictionary<string, int>(loaded, StringComparer.Ordinal);
                }
            }
            catch (Exception ex)
            {
                warn?.Invoke($"failed to load map_plays.json: {ex.Message}");
            }
        }

        public int CountOf(string map)
        {
            lock (_gate)
            {
                return _counts.TryGetValue(map, out int n) ? n : 0;
            }
        }

        public void Increment(string map)
        {
            if (string.IsNullOrWhiteSpace(map))
            {
                return;
            }

            lock (_gate)
            {
                _counts.TryGetValue(map, out int n);
                _counts[map] = n + 1;
                _dirty = true;
            }
        }

        public void Save(PluginConfig config, Action<string>? warn = null)
        {
            Dictionary<string, int> snapshot;
            lock (_gate)
            {
                if (!_dirty)
                {
                    return;
                }

                snapshot = new Dictionary<string, int>(_counts, StringComparer.Ordinal);
            }

            try
            {
                string path = FilePath(config);
                string? dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                PlayerFiles.WriteBytes(path, JsonSerializer.SerializeToUtf8Bytes(snapshot, JsonOptions));
                lock (_gate)
                {
                    _dirty = false;
                }
            }
            catch (Exception ex)
            {
                warn?.Invoke($"failed to save map_plays.json: {ex.Message}");
            }
        }
    }
}
