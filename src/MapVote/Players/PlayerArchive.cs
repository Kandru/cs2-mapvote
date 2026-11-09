using MapVote.Config;

namespace MapVote.Players
{
    /// <summary>Steam-id keyed player favorites and ratings. Memory during the map; disk at map boundaries.</summary>
    public sealed class PlayerArchive
    {
        private readonly Dictionary<string, PlayerState> _bySteam = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _blockedWrites = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _likes = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _dislikes = new(StringComparer.Ordinal);

        public void Load(PluginConfig config, Action<string>? warn = null, Action<string>? debug = null)
        {
            _bySteam.Clear();
            _blockedWrites.Clear();
            _likes.Clear();
            _dislikes.Clear();

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

                // Prefer the file with more persisted state; equal → newer write time.
                int existingWeight = existing.Favorites.Count + existing.Likes.Count + existing.Dislikes.Count;
                int incomingWeight = state.Favorites.Count + state.Likes.Count + state.Dislikes.Count;
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

            RebuildRatingCounts(debug);

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

        public PlayerRating RatingOf(string? steamId, string map) =>
            TryGet(steamId) is { } state ? RatingOf(state, map) : PlayerRating.None;

        public static PlayerRating RatingOf(PlayerState state, string map)
        {
            if (state.Likes.Contains(map, StringComparer.Ordinal))
            {
                return PlayerRating.Like;
            }

            return state.Dislikes.Contains(map, StringComparer.Ordinal)
                ? PlayerRating.Dislike
                : PlayerRating.None;
        }

        /// <summary>
        /// Set a player's rating for one map. Like/dislike are mutually exclusive.
        /// Returns false when the state did not change.
        /// </summary>
        public bool SetRating(string steamId, string map, PlayerRating rating)
        {
            PlayerState state = GetOrCreate(steamId);
            PlayerRating previous = RatingOf(state, map);
            if (previous == rating)
            {
                return false;
            }

            RemoveFromList(state.Likes, map, _likes);
            RemoveFromList(state.Dislikes, map, _dislikes);

            switch (rating)
            {
                case PlayerRating.Like:
                    state.Likes.Add(map);
                    Increment(_likes, map);
                    break;
                case PlayerRating.Dislike:
                    state.Dislikes.Add(map);
                    Increment(_dislikes, map);
                    break;
            }

            state.Dirty = true;
            return true;
        }

        public int LikeCount(string map) => _likes.GetValueOrDefault(map);

        public int DislikeCount(string map) => _dislikes.GetValueOrDefault(map);

        public int RatingTotal(string map) => LikeCount(map) + DislikeCount(map);

        /// <summary>Net people score used by the most-liked / least-liked sorts.</summary>
        public int RatingScore(string map) => LikeCount(map) - DislikeCount(map);

        private static void RemoveFromList(
            List<string> maps,
            string map,
            Dictionary<string, int> counts)
        {
            int index = maps.FindIndex(m => string.Equals(m, map, StringComparison.Ordinal));
            if (index < 0)
            {
                return;
            }

            maps.RemoveAt(index);
            Decrement(counts, map);
        }

        private static void Increment(Dictionary<string, int> counts, string map) =>
            counts[map] = counts.GetValueOrDefault(map) + 1;

        private static void Decrement(Dictionary<string, int> counts, string map)
        {
            int next = counts.GetValueOrDefault(map) - 1;
            if (next > 0)
            {
                counts[map] = next;
            }
            else
            {
                counts.Remove(map);
            }
        }

        /// <summary>Rebuild aggregate counts from player files and repair malformed lists.</summary>
        private void RebuildRatingCounts(Action<string>? debug)
        {
            _likes.Clear();
            _dislikes.Clear();
            int repaired = 0;

            foreach (PlayerState state in _bySteam.Values)
            {
                repaired += Normalize(state.Likes);
                repaired += Normalize(state.Dislikes);

                // A map may never be both liked and disliked. Likes win on legacy/corrupt files.
                for (int i = state.Dislikes.Count - 1; i >= 0; i--)
                {
                    if (state.Likes.Contains(state.Dislikes[i], StringComparer.Ordinal))
                    {
                        state.Dislikes.RemoveAt(i);
                        repaired++;
                    }
                }

                foreach (string map in state.Likes)
                {
                    Increment(_likes, map);
                }

                foreach (string map in state.Dislikes)
                {
                    Increment(_dislikes, map);
                }
            }

            if (repaired > 0)
            {
                debug?.Invoke($"player archive: repaired {repaired} rating entries");
            }
        }

        private static int Normalize(List<string> maps)
        {
            HashSet<string> seen = new(StringComparer.Ordinal);
            int removed = 0;
            for (int i = maps.Count - 1; i >= 0; i--)
            {
                string map = maps[i];
                if (string.IsNullOrWhiteSpace(map) || !seen.Add(map))
                {
                    maps.RemoveAt(i);
                    removed++;
                }
            }

            return removed;
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
                debug?.Invoke(
                    $"saved {state.Username} ({steamId}) favorites={state.Favorites.Count} "
                    + $"likes={state.Likes.Count} dislikes={state.Dislikes.Count}");
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
