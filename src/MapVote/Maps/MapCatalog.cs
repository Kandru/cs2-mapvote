using MapVote.Huds;
using MapVote.Utils;

namespace MapVote.Maps
{
    public sealed class MapCatalog
    {
        /// <summary>Selection cap, tied to the HUD's fixed grid pool.</summary>
        public const int MaxMaps = VoteIds.SlotCount;
        public const int RetryPauseMilliseconds = 400;
        public const int MaxCommandTimeoutMilliseconds = 3000;

        private readonly Action<string> _log;
        private readonly Action<IReadOnlyList<MapEntry>, int>? _onLoaded;
        private readonly object _gate = new();
        private IReadOnlyList<MapEntry> _maps = [];
        private IReadOnlyDictionary<string, string> _workshopNamesById =
            new Dictionary<string, string>(StringComparer.Ordinal);
        private bool _loading;
        private bool _loggedMissingPassword;
        private bool _loggedSourcesDisabled;
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

        /// <summary>
        /// Resolves a numeric workshop id (as some servers report through
        /// <c>Server.MapName</c>) to the map name from the last mapgroup reply.
        /// </summary>
        public bool TryGetWorkshopMapName(string workshopId, out string name)
        {
            lock (_gate)
            {
                if (_workshopNamesById.TryGetValue(workshopId, out string? resolved))
                {
                    name = resolved;
                    return true;
                }
            }

            name = string.Empty;
            return false;
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
        /// Background refresh that retries until the enabled RCON replies look ready or
        /// <paramref name="budgetSeconds"/> elapses. Never replaces a previous good list
        /// with an empty result. <c>0</c> budget = one attempt.
        /// </summary>
        public void RequestRefresh(
            int budgetSeconds = 0,
            bool queryLocalMaps = true,
            bool queryMapgroup = true)
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

            if (!queryLocalMaps && !queryMapgroup)
            {
                if (!_loggedSourcesDisabled)
                {
                    _loggedSourcesDisabled = true;
                    _log("catalog sources disabled (catalog.query_local_maps=false, catalog.query_mapgroup=false); keeping previous list.");
                }

                NotifyCurrent(generation);
                ClearLoading(generation);
                return;
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
            string sources = DescribeSources(queryLocalMaps, queryMapgroup);
            _log($"catalog refresh started (sources={sources}, budget={budget}s generation={generation})");
            _ = Task.Run(() => LoadWorker(
                port,
                password,
                budget,
                generation,
                queryLocalMaps,
                queryMapgroup));
        }

        private static string DescribeSources(bool queryLocalMaps, bool queryMapgroup) =>
            (queryLocalMaps, queryMapgroup) switch
            {
                (true, true) => "local+mapgroup",
                (true, false) => "local",
                (false, true) => "mapgroup",
                _ => "none",
            };

        private void LoadWorker(
            int port,
            string password,
            int budgetSeconds,
            int generation,
            bool queryLocalMaps,
            bool queryMapgroup)
        {
            DateTime deadlineUtc = DateTime.UtcNow.AddSeconds(budgetSeconds);
            List<MapEntry>? bestMaps = null;
            IReadOnlyDictionary<string, string> bestWorkshopNamesById =
                MapGroupParseResult.Empty.WorkshopNamesById;
            string? bestSummary = null;
            string sources = DescribeSources(queryLocalMaps, queryMapgroup);

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

                        string localBody = queryLocalMaps ? client.SendCommand("maps *") : string.Empty;
                        string mapgroupBody = queryMapgroup
                            ? client.SendCommand("print_mapgroup_sv")
                            : string.Empty;

                        (List<MapEntry> localBodyMaps, int localBodySkipped) = queryLocalMaps
                            ? ParseLocalMaps(localBody)
                            : ([], 0);
                        bool localReady = !queryLocalMaps
                            || localBodyMaps.Count > 0
                            || localBodySkipped > 0;

                        MapGroupParseResult mapgroup = queryMapgroup
                            ? MapGroupList.Parse(mapgroupBody)
                            : MapGroupParseResult.Empty;
                        bool mapgroupReady = !queryMapgroup || mapgroup.Ready;

                        (List<MapEntry> local, int mapgroupLocalSkipped) =
                            Merge(localBodyMaps, mapgroup.Local);
                        (List<MapEntry> maps, int workshopOverlapSkipped) =
                            Merge(local, mapgroup.Workshop);

                        int localSkipped = localBodySkipped + mapgroupLocalSkipped;
                        int workshopSkipped = mapgroup.Skipped + workshopOverlapSkipped;
                        string summary =
                            $"sources={sources}, local={local.Count}, local_skipped={localSkipped}, "
                            + $"mapgroup_local={mapgroup.Local.Count}, workshop={mapgroup.Workshop.Count}, "
                            + $"workshop_skipped={workshopSkipped}";

                        if (maps.Count > 0)
                        {
                            bestMaps = maps;
                            if (mapgroupReady)
                            {
                                bestWorkshopNamesById = mapgroup.WorkshopNamesById;
                            }

                            bestSummary = summary;
                        }

                        if (localReady && mapgroupReady)
                        {
                            if (maps.Count == 0)
                            {
                                _log($"catalog ready but empty ({summary}); keeping previous list");
                                break;
                            }

                            Publish(
                                maps,
                                mapgroup.WorkshopNamesById,
                                generation,
                                summary,
                                partial: false);
                            return;
                        }

                        _log(
                            $"catalog not ready (local_ready={localReady}, mapgroup_ready={mapgroupReady}, {summary}); retrying");
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
                    Publish(
                        bestMaps,
                        bestWorkshopNamesById,
                        generation,
                        bestSummary ?? "partial",
                        partial: true);
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

        private void Publish(
            List<MapEntry> maps,
            IReadOnlyDictionary<string, string> workshopNamesById,
            int generation,
            string summary,
            bool partial)
        {
            lock (_gate)
            {
                if (generation != _generation)
                {
                    return;
                }

                _maps = maps;
                _workshopNamesById = workshopNamesById;
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
        /// Keeps <paramref name="local"/> first, then appends new workshop names.
        /// Names present in both keep their local origin.
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

        private static (List<MapEntry> Maps, int Skipped) ParseLocalMaps(string response)
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
                MapLineKind kind = MapNameFilter.ClassifyLocalLine(line, out string? name);
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
                            maps.Add(new MapEntry(name, MapOrigin.Local));
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

        private static bool TryGetRconCredentials(out int port, out string password)
        {
            port = ConVars.ReadInt("hostport") ?? 0;
            password = ConVars.ReadString("rcon_password") ?? string.Empty;
            return port > 0 && password.Length > 0;
        }
    }
}
