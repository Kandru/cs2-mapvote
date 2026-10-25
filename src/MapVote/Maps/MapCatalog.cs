using MapVote.Utils;

namespace MapVote.Maps
{
    public sealed class MapCatalog
    {
        public const int MaxMaps = 100;

        private readonly Action<string> _log;
        private readonly Action<IReadOnlyList<MapEntry>>? _onLoaded;
        private readonly object _gate = new();
        private IReadOnlyList<MapEntry> _maps = [];
        private bool _loading;
        private bool _loggedMissingPassword;

        public MapCatalog(Action<string> log, Action<IReadOnlyList<MapEntry>>? onLoaded = null)
        {
            _log = log;
            _onLoaded = onLoaded;
        }

        public IReadOnlyList<MapEntry> Maps
        {
            get { lock (_gate) { return _maps; } }
        }

        public bool IsLoading
        {
            get { lock (_gate) { return _loading; } }
        }

        public void RequestRefresh()
        {
            lock (_gate)
            {
                if (_loading)
                {
                    return;
                }

                _loading = true;
            }

            if (!TryGetRconCredentials(out int port, out string password))
            {
                lock (_gate) { _loading = false; }

                if (!_loggedMissingPassword)
                {
                    _loggedMissingPassword = true;
                    _log("rcon_password empty; cannot load maps.");
                }

                return;
            }

            _log("catalog refresh started");
            _ = Task.Run(() => LoadWorker(port, password));
        }

        private void LoadWorker(int port, string password)
        {
            try
            {
                using RconClient client = new("127.0.0.1", port, password, 1000);
                client.Connect();

                List<MapEntry> local = ParseMaps(
                    client.SendCommand("maps *"), MapOrigin.Local, MapNameFilter.NormalizeLocalLine);
                List<MapEntry> workshop = ParseMaps(
                    client.SendCommand("print_mapgroup_sv"),
                    MapOrigin.Workshop,
                    MapNameFilter.NormalizeMapgroupLine);

                List<MapEntry> maps = Merge(local, workshop);

                lock (_gate)
                {
                    _maps = maps;
                }

                _log($"loaded {maps.Count} maps (local={local.Count}, workshop={workshop.Count}).");
                _onLoaded?.Invoke(maps);
            }
            catch (Exception ex)
            {
                _log($"map load failed: {ex.Message}");
            }
            finally
            {
                lock (_gate) { _loading = false; }
            }
        }

        /// <summary>
        /// Local maps first (keep their order), then mapgroup-only names as workshop.
        /// Names present in both keep Local origin.
        /// </summary>
        internal static List<MapEntry> Merge(IReadOnlyList<MapEntry> local, IReadOnlyList<MapEntry> workshop)
        {
            List<MapEntry> maps = new(local.Count + workshop.Count);
            HashSet<string> seen = new(StringComparer.Ordinal);
            AppendUnique(maps, seen, local);
            AppendUnique(maps, seen, workshop);
            return maps;
        }

        private static void AppendUnique(
            List<MapEntry> maps,
            HashSet<string> seen,
            IReadOnlyList<MapEntry> source)
        {
            foreach (MapEntry entry in source)
            {
                if (seen.Add(entry.Name))
                {
                    maps.Add(entry);
                }
            }
        }

        private static List<MapEntry> ParseMaps(
            string response,
            MapOrigin origin,
            Func<string, string?> normalize)
        {
            if (string.IsNullOrEmpty(response))
            {
                return [];
            }

            List<MapEntry> maps = [];
            HashSet<string> seen = new(StringComparer.Ordinal);
            foreach (string line in response.Split('\n'))
            {
                string? name = normalize(line);
                if (name is not null && seen.Add(name))
                {
                    maps.Add(new MapEntry(name, origin));
                }
            }

            return maps;
        }

        private static bool TryGetRconCredentials(out int port, out string password)
        {
            port = ConVars.ReadInt("hostport") ?? 0;
            password = ConVars.ReadString("rcon_password") ?? string.Empty;
            return port > 0 && password.Length > 0;
        }
    }
}
