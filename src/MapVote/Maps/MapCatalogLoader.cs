using MapVote.Runtime;

namespace MapVote.Maps
{
    /// <summary>One completed catalog attempt that is safe to publish.</summary>
    internal sealed record CatalogLoadResult(
        List<MapEntry> Maps,
        IReadOnlyDictionary<string, string> WorkshopNamesById,
        string Summary,
        bool Ready);

    /// <summary>
    /// Runs one background catalog refresh: queries the enabled RCON sources, retries until
    /// the replies look usable or the budget expires, and hands the best result to the
    /// catalog. All state shared with the catalog goes through callbacks.
    /// </summary>
    internal sealed class MapCatalogLoader
    {
        private readonly int _port;
        private readonly string _password;
        private readonly CatalogRequest _request;
        private readonly int _generation;
        private readonly Action<string> _info;
        private readonly Action<string> _debug;
        private readonly Func<int, bool> _isCurrent;
        private readonly Action<CatalogLoadResult> _publish;
        private readonly Action<int> _notifyCurrent;
        private readonly Action<int> _finished;

        public MapCatalogLoader(
            int port,
            string password,
            CatalogRequest request,
            int generation,
            Action<string> info,
            Action<string> debug,
            Func<int, bool> isCurrent,
            Action<CatalogLoadResult> publish,
            Action<int> notifyCurrent,
            Action<int> finished)
        {
            _port = port;
            _password = password;
            _request = request;
            _generation = generation;
            _info = info;
            _debug = debug;
            _isCurrent = isCurrent;
            _publish = publish;
            _notifyCurrent = notifyCurrent;
            _finished = finished;
        }

        public void Run()
        {
            DateTime deadlineUtc = DateTime.UtcNow.AddSeconds(_request.BudgetSeconds);
            CatalogLoadResult? best = null;
            IReadOnlyDictionary<string, string> bestWorkshopNamesById =
                MapGroupParseResult.Empty.WorkshopNamesById;
            string sources = _request.DescribeSources();
            int attempt = 0;

            try
            {
                while (_isCurrent(_generation))
                {
                    int remainingMs = RemainingMilliseconds(deadlineUtc);
                    if (_request.BudgetSeconds > 0 && remainingMs <= 0)
                    {
                        break;
                    }

                    int timeoutMs = _request.BudgetSeconds > 0
                        ? Math.Clamp(remainingMs, 1, MapCatalog.MaxCommandTimeoutMilliseconds)
                        : MapCatalog.MaxCommandTimeoutMilliseconds;

                    attempt++;
                    _debug(
                        $"catalog: attempt {attempt} generation={_generation} timeout={timeoutMs}ms remaining={Math.Max(0, remainingMs)}ms");
                    try
                    {
                        using RconClient client = new("127.0.0.1", _port, _password, timeoutMs);
                        client.Connect();

                        string localReply = _request.QueryLocalMaps
                            ? client.SendCommand("maps *")
                            : string.Empty;
                        string mapgroupBody = _request.QueryMapgroup
                            ? client.SendCommand("print_mapgroup_sv")
                            : string.Empty;

                        LocalMapParseResult localBody = _request.QueryLocalMaps
                            ? MapCatalogParser.ParseLocalMaps(localReply)
                            : LocalMapParseResult.Empty;
                        bool localReady = !_request.QueryLocalMaps || localBody.Ready;

                        MapGroupParseResult mapgroup = _request.QueryMapgroup
                            ? MapGroupList.Parse(mapgroupBody)
                            : MapGroupParseResult.Empty;
                        bool mapgroupReady = !_request.QueryMapgroup || mapgroup.Ready;

                        (List<MapEntry> local, int mapgroupLocalSkipped) =
                            MapCatalogParser.Merge(localBody.Maps, mapgroup.Local);
                        (List<MapEntry> maps, int workshopOverlapSkipped) =
                            MapCatalogParser.Merge(local, mapgroup.Workshop);

                        int localSkipped = localBody.Skipped + mapgroupLocalSkipped;
                        int workshopSkipped = mapgroup.Skipped + workshopOverlapSkipped;
                        string summary =
                            $"sources={sources}, local={local.Count}, local_skipped={localSkipped}, "
                            + $"mapgroup_local={mapgroup.Local.Count}, workshop={mapgroup.Workshop.Count}, "
                            + $"workshop_skipped={workshopSkipped}";

                        CatalogLoadResult result = new(
                            maps,
                            mapgroupReady
                                ? mapgroup.WorkshopNamesById
                                : bestWorkshopNamesById,
                            summary,
                            Ready: localReady && mapgroupReady);

                        if (maps.Count > 0)
                        {
                            if (mapgroupReady)
                            {
                                bestWorkshopNamesById = mapgroup.WorkshopNamesById;
                            }

                            best = result;
                        }

                        if (result.Ready)
                        {
                            if (maps.Count == 0)
                            {
                                _info($"catalog: RCON is ready but returned no maps ({summary}); keeping previous list");
                                break;
                            }

                            _publish(result);
                            return;
                        }

                        _debug(
                            $"catalog: attempt {attempt} not ready (local_ready={localReady}, mapgroup_ready={mapgroupReady}); {summary}");
                    }
                    catch (Exception ex)
                    {
                        _debug($"catalog: attempt {attempt} failed: {ex.Message}");
                    }

                    if (_request.BudgetSeconds <= 0)
                    {
                        break;
                    }

                    remainingMs = RemainingMilliseconds(deadlineUtc);
                    if (remainingMs <= 0)
                    {
                        break;
                    }

                    Thread.Sleep(Math.Min(MapCatalog.RetryPauseMilliseconds, remainingMs));
                }

                if (!_isCurrent(_generation))
                {
                    _debug($"catalog: worker for generation {_generation} is stale; discarding {attempt} attempt(s)");
                    return;
                }

                if (best is { Maps.Count: > 0 })
                {
                    _publish(best with { Ready = false });
                    return;
                }

                _info($"catalog: refresh ended with no maps after {attempt} attempt(s); keeping previous list");
                _notifyCurrent(_generation);
            }
            finally
            {
                _finished(_generation);
            }
        }

        private static int RemainingMilliseconds(DateTime deadlineUtc) =>
            (int)Math.Ceiling((deadlineUtc - DateTime.UtcNow).TotalMilliseconds);
    }
}
