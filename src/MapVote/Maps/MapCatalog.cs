using MapVote.Huds;
using MapVote.Runtime;
using MapVote.Utils;

namespace MapVote.Maps
{
    public sealed class MapCatalog
    {
        /// <summary>Selection cap, tied to the HUD's fixed grid pool.</summary>
        public const int MaxMaps = VoteIds.SlotCount;
        public const int RetryPauseMilliseconds = GameConstants.CatalogRetryPauseMilliseconds;
        public const int MaxCommandTimeoutMilliseconds = GameConstants.MaxCommandTimeoutMilliseconds;

        private readonly Action<string> _log;
        private readonly Action<IReadOnlyList<MapEntry>, int>? _onLoaded;
        private readonly object _gate = new();
        private IReadOnlyList<MapEntry> _maps = [];
        private IReadOnlyDictionary<string, string> _workshopNamesById =
            new Dictionary<string, string>(StringComparer.Ordinal);
        private bool _loading;
        // A request that arrived while the worker was busy is coalesced and run afterwards.
        private CatalogRequest? _queued;
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
                // Work queued for the previous generation is stale too.
                _queued = null;
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
            CatalogRequest request = CatalogRequest.Create(budgetSeconds, queryLocalMaps, queryMapgroup);
            int generation;
            lock (_gate)
            {
                if (_loading)
                {
                    // Do not drop a newer request (for example !maps during the intermission
                    // query): remember it and start it when the current worker finishes.
                    _queued = (_queued ?? request).Coalesce(request);
                    return;
                }

                _loading = true;
                generation = _generation;
            }

            if (!request.QueryLocalMaps && !request.QueryMapgroup)
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

            _log($"catalog refresh started (sources={request.DescribeSources()}, budget={request.BudgetSeconds}s generation={generation})");
            _ = Task.Run(() => LoadWorker(port, password, request, generation));
        }

        private void LoadWorker(int port, string password, CatalogRequest request, int generation)
        {
            MapCatalogLoader loader = new(
                port,
                password,
                request,
                generation,
                _log,
                IsCurrentGeneration,
                result => Publish(result, generation),
                NotifyCurrent,
                ClearLoading);
            loader.Run();
        }

        private void Publish(CatalogLoadResult result, int generation)
        {
            lock (_gate)
            {
                if (generation != _generation)
                {
                    return;
                }

                _maps = result.Maps;
                _workshopNamesById = result.WorkshopNamesById;
            }

            _log($"loaded {result.Maps.Count} maps ({(result.Ready ? "ready" : "partial")}; {result.Summary}).");
            _onLoaded?.Invoke(result.Maps, generation);
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
            CatalogRequest? queued = null;
            lock (_gate)
            {
                // A stale worker must not touch state owned by the current generation.
                if (generation == _generation)
                {
                    _loading = false;
                    queued = _queued;
                    _queued = null;
                }
            }

            if (queued is { } request)
            {
                RequestRefresh(request.BudgetSeconds, request.QueryLocalMaps, request.QueryMapgroup);
            }
        }

        private bool IsCurrentGeneration(int generation)
        {
            lock (_gate)
            {
                return generation == _generation;
            }
        }

        /// <summary>
        /// Keeps <paramref name="local"/> first, then appends new workshop names.
        /// Names present in both keep their local origin.
        /// </summary>
        internal static (List<MapEntry> Maps, int WorkshopSkipped) Merge(
            IReadOnlyList<MapEntry> local,
            IReadOnlyList<MapEntry> workshop) =>
            MapCatalogParser.Merge(local, workshop);

        private static bool TryGetRconCredentials(out int port, out string password)
        {
            port = ConVars.ReadInt("hostport") ?? 0;
            password = ConVars.ReadString("rcon_password") ?? string.Empty;
            return port > 0 && password.Length > 0;
        }
    }
}
