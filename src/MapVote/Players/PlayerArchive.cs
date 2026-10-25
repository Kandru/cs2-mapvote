using MapVote.Config;

namespace MapVote.Players
{
    /// <summary>Steam-id keyed player favorites. Memory during the map; disk at map boundaries.</summary>
    public sealed class PlayerArchive
    {
        private readonly Dictionary<string, PlayerState> _bySteam = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _blockedWrites = new(StringComparer.OrdinalIgnoreCase);

        public void Load(PluginConfig config, Action<string>? warn = null, Action<string>? debug = null)
        {
            _bySteam.Clear();
            _blockedWrites.Clear();

            string dir = PlayerFiles.DirectoryPath(config);
            if (!Directory.Exists(dir))
            {
                debug?.Invoke("player archive: no players directory");
                return;
            }

            string[] paths = Directory.GetFiles(dir, "*.json");
            if (paths.Length == 0)
            {
                debug?.Invoke("player archive: 0 loaded");
                return;
            }

            var loaded = new (string Path, PlayerState? State, DateTime WriteTime, Exception? Error)[paths.Length];
            if (paths.Length < 4)
            {
                for (int i = 0; i < paths.Length; i++)
                {
                    loaded[i] = ReadOne(paths[i]);
                }
            }
            else
            {
                Parallel.For(0, paths.Length, i => loaded[i] = ReadOne(paths[i]));
            }

            int bad = 0;
            var writeTimes = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
            foreach ((string path, PlayerState? state, DateTime writeTime, Exception? error) in loaded)
            {
                if (error != null || state is null)
                {
                    bad++;
                    QuarantineOrBlock(path, Path.GetFileNameWithoutExtension(path), warn, debug, error);
                    continue;
                }

                string steamId = state.SteamId.Length > 0
                    ? state.SteamId
                    : Path.GetFileNameWithoutExtension(path);
                if (steamId.Length == 0)
                {
                    bad++;
                    warn?.Invoke($"player file missing steam id: {path}");
                    continue;
                }

                state.SteamId = steamId;
                if (state.Username.Length == 0)
                {
                    state.Username = steamId;
                }

                if (!_bySteam.TryGetValue(steamId, out PlayerState? existing))
                {
                    _bySteam[steamId] = state;
                    writeTimes[steamId] = writeTime;
                    continue;
                }

                // Prefer the file with more favorites; equal → newer write time.
                int existingWeight = existing.Favorites.Count;
                int incomingWeight = state.Favorites.Count;
                if (incomingWeight > existingWeight
                    || (incomingWeight == existingWeight && writeTime > writeTimes.GetValueOrDefault(steamId)))
                {
                    debug?.Invoke($"duplicate player file for {steamId}; keeping {Path.GetFileName(path)}");
                    _bySteam[steamId] = state;
                    writeTimes[steamId] = writeTime;
                }
                else
                {
                    debug?.Invoke($"duplicate player file for {steamId}; ignoring {Path.GetFileName(path)}");
                }
            }

            debug?.Invoke(
                bad > 0
                    ? $"player archive: {_bySteam.Count} loaded, {bad} failed"
                    : $"player archive: {_bySteam.Count} loaded");
        }

        public PlayerState? TryGet(string? steamId) =>
            !string.IsNullOrEmpty(steamId) && _bySteam.TryGetValue(steamId, out PlayerState? state)
                ? state
                : null;

        public PlayerState GetOrCreate(string steamId)
        {
            if (_bySteam.TryGetValue(steamId, out PlayerState? state))
            {
                return state;
            }

            state = new PlayerState { SteamId = steamId, Username = steamId };
            _bySteam[steamId] = state;
            return state;
        }

        public int SaveDirty(PluginConfig config, Action<string>? warn = null, Action<string>? debug = null)
        {
            string directory = PlayerFiles.DirectoryPath(config);
            int written = 0;
            foreach (PlayerState state in _bySteam.Values)
            {
                if (!state.Dirty)
                {
                    continue;
                }

                if (TryWrite(directory, state, warn, debug))
                {
                    written++;
                }
            }

            return written;
        }

        private bool TryWrite(string directory, PlayerState state, Action<string>? warn, Action<string>? debug)
        {
            string steamId = state.SteamId;
            if (steamId.Length == 0)
            {
                warn?.Invoke($"skip save: empty steam id (user '{state.Username}')");
                return false;
            }

            if (_blockedWrites.Contains(steamId))
            {
                warn?.Invoke($"skip save: writes blocked for {steamId} (corrupt file left in place)");
                return false;
            }

            try
            {
                PlayerFiles.Write(PlayerFiles.FilePath(directory, steamId), state);
                state.Dirty = false;
                debug?.Invoke($"saved {state.Username} ({steamId}) favorites={state.Favorites.Count}");
                return true;
            }
            catch (Exception ex)
            {
                warn?.Invoke($"failed to save player {steamId}: {ex.Message}");
                state.Dirty = true;
                return false;
            }
        }

        private static (string Path, PlayerState? State, DateTime WriteTime, Exception? Error) ReadOne(string path)
        {
            try
            {
                return (path, PlayerFiles.DeserializeBytes(File.ReadAllBytes(path)), File.GetLastWriteTimeUtc(path), null);
            }
            catch (Exception ex)
            {
                return (path, null, default, ex);
            }
        }

        private void QuarantineOrBlock(
            string path,
            string steamIdFromPath,
            Action<string>? warn,
            Action<string>? debug,
            Exception? error)
        {
            string detail = error?.Message ?? "null state";
            if (PlayerFiles.TryQuarantine(path, out string? quarantinePath))
            {
                warn?.Invoke($"quarantined corrupt player file {path} -> {quarantinePath}: {detail}");
                debug?.Invoke($"corrupt player file: {path} ({detail})");
                return;
            }

            if (steamIdFromPath.Length > 0)
            {
                _blockedWrites.Add(steamIdFromPath);
            }

            warn?.Invoke($"corrupt player file left in place; writes blocked for {steamIdFromPath}: {detail}");
        }
    }
}
