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

                (List<MapEntry> local, int localSkipped) = ParseMaps(
                    client.SendCommand("maps *"), MapOrigin.Local, MapNameFilter.ClassifyLocalLine);
                (List<MapEntry> workshop, int workshopFilterSkipped) = ParseMaps(
                    client.SendCommand("print_mapgroup_sv"),
                    MapOrigin.Workshop,
                    MapNameFilter.ClassifyMapgroupLine);

                (List<MapEntry> maps, int workshopOverlapSkipped) = Merge(local, workshop);
                int workshopSkipped = workshopFilterSkipped + workshopOverlapSkipped;

                lock (_gate)
                {
                    _maps = maps;
                }

                _log(
                    $"loaded {maps.Count} maps (local={local.Count}, local_skipped={localSkipped}, workshop={workshop.Count}, workshop_skipped={workshopSkipped}).");
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
        /// <returns>Merged list and how many workshop entries were omitted as duplicates of local.</returns>
        internal static (List<MapEntry> Maps, int WorkshopSkipped) Merge(
            IReadOnlyList<MapEntry> local,
            IReadOnlyList<MapEntry> workshop)
        {
            List<MapEntry> maps = new(local.Count + workshop.Count);
            HashSet<string> seen = new(StringComparer.Ordinal);
            AppendUnique(maps, seen, local);
            int workshopSkipped = AppendUnique(maps, seen, workshop);
            return (maps, workshopSkipped);
        }

        /// <returns>Number of source entries that were not appended (already seen).</returns>
        private static int AppendUnique(
            List<MapEntry> maps,
            HashSet<string> seen,
            IReadOnlyList<MapEntry> source)
        {
            int skipped = 0;
            foreach (MapEntry entry in source)
            {
                if (seen.Add(entry.Name))
                {
                    maps.Add(entry);
                }
                else
                {
                    skipped++;
                }
            }

            return skipped;
        }

        private static (List<MapEntry> Maps, int Skipped) ParseMaps(
            string response,
            MapOrigin origin,
            ClassifyLine classify)
        {
            if (string.IsNullOrEmpty(response))
            {
                return ([], 0);
            }

            List<MapEntry> maps = [];
            HashSet<string> seen = new(StringComparer.Ordinal);
            int skipped = 0;
            foreach (string line in response.Split('\n'))
            {
                MapLineKind kind = classify(line, out string? name);
                switch (kind)
                {
                    case MapLineKind.Ignore:
                        break;
                    case MapLineKind.Skip:
                        skipped++;
                        break;
                    case MapLineKind.Accept:
                        if (name is not null && seen.Add(name))
                        {
                            maps.Add(new MapEntry(name, origin));
                        }
                        else
                        {
                            skipped++;
                        }

                        break;
                }
            }

            return (maps, skipped);
        }

        private delegate MapLineKind ClassifyLine(string line, out string? name);

        private static bool TryGetRconCredentials(out int port, out string password)
        {
            port = ConVars.ReadInt("hostport") ?? 0;
            password = ConVars.ReadString("rcon_password") ?? string.Empty;
            return port > 0 && password.Length > 0;
        }
    }
}
