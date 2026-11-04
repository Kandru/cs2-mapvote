using MapVote.Utils;

namespace MapVote.Maps
{
    public sealed class MapCatalog
    {
        public const int MaxMaps = 100;
        public const int RetryPauseMilliseconds = 400;
        public const int MaxCommandTimeoutMilliseconds = 3000;

        private readonly Action<string> _log;
        private readonly Action<IReadOnlyList<MapEntry>, int>? _onLoaded;
        private readonly object _gate = new();
        private IReadOnlyList<MapEntry> _maps = [];
        private bool _loading;
        private bool _loggedMissingPassword;
        private int _generation;

        public MapCatalog(Action<string> log, Action<IReadOnlyList<MapEntry>, int>? onLoaded = null)
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

        public int Generation
        {
            get { lock (_gate) { return _generation; } }
        }

        /// <summary>
        /// Bumps the generation so a late RCON callback from a previous map is ignored.
        /// </summary>
        public void Invalidate()
        {
            lock (_gate)
            {
                _generation++;
                _loading = false;
            }
        }

        /// <summary>
        /// Background refresh that retries until both RCON replies look ready or
        /// <paramref name="budgetSeconds"/> elapses. Never replaces a previous good list
        /// with an empty result. <c>0</c> budget = one attempt.
        /// </summary>
        public void RequestRefresh(int budgetSeconds = 0)
        {
            int generation;
            lock (_gate)
            {
                if (_loading)
                {
                    return;
                }

                _loading = true;
                generation = _generation;
            }

            if (!TryGetRconCredentials(out int port, out string password))
            {
                if (!_loggedMissingPassword)
                {
                    _loggedMissingPassword = true;
                    _log("rcon_password empty; cannot load maps.");
                }

                NotifyCurrent(generation);
                ClearLoading(generation);
                return;
            }

            int budget = Math.Max(0, budgetSeconds);
            _log($"catalog refresh started (budget={budget}s generation={generation})");
            _ = Task.Run(() => LoadWorker(port, password, budget, generation));
        }

        private void LoadWorker(int port, string password, int budgetSeconds, int generation)
        {
            DateTime deadlineUtc = DateTime.UtcNow.AddSeconds(budgetSeconds);
            List<MapEntry>? bestMaps = null;
            string? bestSummary = null;

            try
            {
                while (IsCurrentGeneration(generation))
                {
                    int remainingMs = RemainingMilliseconds(deadlineUtc);
                    if (budgetSeconds > 0 && remainingMs <= 0)
                    {
                        break;
                    }

                    int timeoutMs = budgetSeconds > 0
                        ? Math.Clamp(remainingMs, 1, MaxCommandTimeoutMilliseconds)
                        : MaxCommandTimeoutMilliseconds;

                    try
                    {
                        using RconClient client = new("127.0.0.1", port, password, timeoutMs);
                        client.Connect();

                        string localBody = client.SendCommand("maps *");
                        string workshopBody = client.SendCommand("print_mapgroup_sv");
                        bool localReady = MapListReadiness.IsLocalReady(localBody);
                        bool workshopReady = MapListReadiness.IsMapgroupReady(workshopBody);

                        (List<MapEntry> local, int localSkipped) = ParseMaps(
                            localBody, MapOrigin.Local, MapNameFilter.ClassifyLocalLine);
                        (List<MapEntry> workshop, int workshopFilterSkipped) = ParseMaps(
                            workshopBody, MapOrigin.Workshop, MapNameFilter.ClassifyMapgroupLine);
                        (List<MapEntry> maps, int workshopOverlapSkipped) = Merge(local, workshop);

                        string summary =
                            $"local={local.Count}, local_skipped={localSkipped}, workshop={workshop.Count}, workshop_skipped={workshopFilterSkipped + workshopOverlapSkipped}";

                        if (maps.Count > 0)
                        {
                            bestMaps = maps;
                            bestSummary = summary;
                        }

                        if (localReady && workshopReady)
                        {
                            if (maps.Count == 0)
                            {
                                _log($"catalog ready but empty ({summary}); keeping previous list");
                                break;
                            }

                            Publish(maps, generation, summary, partial: false);
                            return;
                        }

                        _log(
                            $"catalog not ready (local_ready={localReady}, workshop_ready={workshopReady}, {summary}); retrying");
                    }
                    catch (Exception ex)
                    {
                        _log($"map load attempt failed: {ex.Message}");
                    }

                    if (budgetSeconds <= 0)
                    {
                        break;
                    }

                    remainingMs = RemainingMilliseconds(deadlineUtc);
                    if (remainingMs <= 0)
                    {
                        break;
                    }

                    Thread.Sleep(Math.Min(RetryPauseMilliseconds, remainingMs));
                }

                if (!IsCurrentGeneration(generation))
                {
                    return;
                }

                if (bestMaps is { Count: > 0 })
                {
                    Publish(bestMaps, generation, bestSummary ?? "partial", partial: true);
                    return;
                }

                _log("catalog refresh ended with no maps; keeping previous list");
                NotifyCurrent(generation);
            }
            finally
            {
                ClearLoading(generation);
            }
        }

        private void Publish(List<MapEntry> maps, int generation, string summary, bool partial)
        {
            lock (_gate)
            {
                if (generation != _generation)
                {
                    return;
                }

                _maps = maps;
            }

            _log($"loaded {maps.Count} maps ({(partial ? "partial" : "ready")}; {summary}).");
            _onLoaded?.Invoke(maps, generation);
        }

        private void NotifyCurrent(int generation)
        {
            IReadOnlyList<MapEntry> current;
            lock (_gate)
            {
                if (generation != _generation)
                {
                    return;
                }

                current = _maps;
            }

            _onLoaded?.Invoke(current, generation);
        }

        private void ClearLoading(int generation)
        {
            lock (_gate)
            {
                if (generation == _generation)
                {
                    _loading = false;
                }
            }
        }

        private bool IsCurrentGeneration(int generation)
        {
            lock (_gate)
            {
                return generation == _generation;
            }
        }

        private static int RemainingMilliseconds(DateTime deadlineUtc) =>
            (int)Math.Ceiling((deadlineUtc - DateTime.UtcNow).TotalMilliseconds);

        /// <summary>
        /// Local maps first (keep their order), then mapgroup-only names as workshop.
        /// Names present in both keep Local origin.
        /// </summary>
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
