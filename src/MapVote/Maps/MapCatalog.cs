using MapVote.Utils;

namespace MapVote.Maps
{
    public sealed class MapCatalog
    {
        public const int MaxMaps = 50;

        private readonly Action<string> _log;
        private readonly object _gate = new();
        private IReadOnlyList<MapEntry> _maps = [];
        private DateTime _loadedUtc = DateTime.MinValue;
        private bool _loading;
        private bool _loggedMissingPassword;

        public MapCatalog(Action<string> log) => _log = log;

        public IReadOnlyList<MapEntry> Maps
        {
            get { lock (_gate) { return _maps; } }
        }

        public bool IsStale(TimeSpan maxAge)
        {
            lock (_gate)
            {
                return _maps.Count == 0 || DateTime.UtcNow - _loadedUtc > maxAge;
            }
        }

        public void RequestRefresh(bool force = false)
        {
            lock (_gate)
            {
                if (_loading)
                {
                    return;
                }

                if (!force && _maps.Count > 0 && DateTime.UtcNow - _loadedUtc < TimeSpan.FromSeconds(5))
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

            _ = Task.Run(() => LoadWorker(port, password));
        }

        private void LoadWorker(int port, string password)
        {
            try
            {
                List<MapEntry> maps = QueryMaps(
                    port, password, "print_mapgroup_sv", MapOrigin.Workshop, MapNameFilter.NormalizeMapgroupLine);
                string source = "mapgroup";

                if (maps.Count == 0)
                {
                    maps = QueryMaps(port, password, "maps *", MapOrigin.Local, MapNameFilter.NormalizeLocalLine);
                    source = "maps *";
                }

                if (maps.Count > MaxMaps)
                {
                    int original = maps.Count;
                    maps = maps.Take(MaxMaps).ToList();
                    _log($"map list truncated from {original} to {MaxMaps}.");
                }

                lock (_gate)
                {
                    _maps = maps;
                    _loadedUtc = DateTime.UtcNow;
                }

                _log($"loaded {maps.Count} maps (source={source}).");
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

        private static List<MapEntry> QueryMaps(
            int port,
            string password,
            string command,
            MapOrigin origin,
            Func<string, string?> normalize)
        {
            using RconClient client = new("127.0.0.1", port, password, 1000);
            client.Connect();
            string response = client.SendCommand(command);
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
