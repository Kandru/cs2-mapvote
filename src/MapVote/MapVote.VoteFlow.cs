using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using MapVote.Huds;
using MapVote.Maps;
using MapVote.Utils;
using MapVote.Voting;

namespace MapVote
{
    public partial class MapVote
    {
        private bool VoteBusy => _voteActive || _revealActive || _catalogPreparing;

        private float RemainingUntil(int offsetSeconds) =>
            VoteClock.SecondsUntil(_scheduleEpochUtc, offsetSeconds, DateTime.UtcNow);

        /// <summary>
        /// Prefer our overlay unless every catalog map is disabled (then enable the Valve vote).
        /// Does not write <c>mp_match_restart_delay</c>.
        /// </summary>
        private void ArmEngineVote()
        {
            if (_catalog.Maps.Count == 0
                || _catalog.IsLoading
                || MapVisibility.AnyShown(_catalog.Maps, Config.MapsVotable))
            {
                EngineVotePrep.PrepareCustomVote();
                _nativeVoteArmed = false;
                PluginLog.Debug("engine vote: custom (native vote off)");
                return;
            }

            if (_nativeVoteArmed)
            {
                return;
            }

            int open = VoteClock.NativeVoteSeconds(Config.Endmatch);
            EngineVotePrep.PrepareNativeVote(open);
            _nativeVoteArmed = true;
            PluginLog.Info($"no votable maps; enabling Valve end-match vote ({open}s)");
        }

        /// <summary>
        /// Win-panel entry: disable the Valve vote, refresh the catalog inside the panel budget,
        /// then open the HUD once that refresh settles.
        /// </summary>
        private void BeginEndMatchFromWinPanel()
        {
            if (VoteBusy)
            {
                PluginLog.Debug("win panel ignored: vote already active or preparing");
                return;
            }

            StopRtvWindow();
            _rtvVoters.Clear();

            _scheduleEpochUtc = DateTime.UtcNow;
            _panelSeconds = VoteClock.ReadPanelSeconds(ConVars.ReadFloat);
            int queryBudget = VoteClock.QueryBudgetSeconds(
                _panelSeconds,
                Math.Max(0, Config.Endmatch.CatalogWaitSeconds),
                Config.Endmatch.WinnerRevealSeconds);

            EngineVotePrep.PrepareCustomVote();
            _nativeVoteArmed = false;
            _catalogPreparing = true;
            _catalogGeneration = _catalog.Generation;

            PluginLog.Debug(
                $"win panel: panel={_panelSeconds}s query_budget={queryBudget}s generation={_catalogGeneration}");

            KillCatalogWaitTimer();
            _catalogWaitTimer = AddTimer(Math.Max(0.25f, queryBudget + 0.5f), OnCatalogWatchdog);
            _catalog.RequestRefresh(queryBudget);
        }

        private void OnCatalogWatchdog()
        {
            _catalogWaitTimer = null;
            if (!_catalogPreparing || _voteActive || _revealActive)
            {
                return;
            }

            PluginLog.Debug("catalog watchdog: refresh did not settle; using current list");
            OnCatalogSettled(_catalog.Maps, _catalogGeneration);
        }

        /// <summary>Main-thread settle after RCON refresh (or watchdog).</summary>
        private void OnCatalogSettled(IReadOnlyList<MapEntry> maps, int generation)
        {
            if (_unloaded || generation != _catalogGeneration || !_catalogPreparing)
            {
                return;
            }

            KillCatalogWaitTimer();
            _catalogPreparing = false;
            SyncMapsVotable(maps);

            float queryElapsed = (float)(DateTime.UtcNow - _scheduleEpochUtc).TotalSeconds;
            _timing = VoteClock.ResolveCustom(
                _panelSeconds,
                Config.Endmatch.WinnerRevealSeconds,
                queryElapsed);

            List<MapEntry> candidates = SelectCandidates();
            if (candidates.Count == 0)
            {
                EnableNativeVote("catalog empty after win-panel refresh");
                return;
            }

            PluginLog.Debug(
                $"win-panel map list ready: query={queryElapsed:0.###}s open≈{_timing.OpenSeconds}s reveal={_timing.RevealSeconds}s changelevel_at={_timing.ChangelevelOffsetSeconds}s");
            BeginVote(candidates);
        }

        private void EnableNativeVote(string reason)
        {
            KillCatalogWaitTimer();
            _catalogPreparing = false;
            int open = VoteClock.NativeVoteSeconds(Config.Endmatch);
            PluginLog.Info($"vote skipped: {reason}; enabling Valve end-match vote ({open}s)");
            EngineVotePrep.PrepareNativeVote(open);
            _nativeVoteArmed = true;
        }

        private void BeginVote(List<MapEntry> maps)
        {
            if (_voteActive || _revealActive)
            {
                return;
            }

            KillCatalogWaitTimer();
            _catalogPreparing = false;
            SuppressNativeVoteSlots();

            _session = new VoteSession(maps.Select(static m => m.Name).ToList());
            _origins = maps.ToDictionary(
                static m => m.Name,
                static m => m.Origin,
                StringComparer.Ordinal);

            VoteHud.Bind(
                _session,
                Config.Filters,
                _archive,
                _plays,
                (player, key) => Localization.ForPlayerSafe(Localizer, player, key),
                _origins);
            _voteActive = true;
            _voteHudOpen = false;
            _revealActive = false;
            _pendingWinner = null;

            PluginLog.Info($"map voting started ({maps.Count} maps)");
            PluginLog.Debug(
                $"vote armed: maps=[{string.Join(", ", maps.Select(static m => m.Name))}] open={_timing.OpenSeconds}s reveal={_timing.RevealSeconds}s changelevel_at={_timing.ChangelevelOffsetSeconds}s");

            ShowVoteHud();
        }

        /// <summary>
        /// Opens the overlay after the catalog settled. Timers are pinned to the win-panel epoch
        /// so <c>changelevel</c> stays at panel − 1s regardless of query length.
        /// </summary>
        private void ShowVoteHud()
        {
            if (!_voteActive || _revealActive || _voteHudOpen || _session is null)
            {
                return;
            }

            float untilReveal = RemainingUntil(_timing.RevealOffsetSeconds);
            float untilChangelevel = RemainingUntil(_timing.ChangelevelOffsetSeconds);

            if (untilChangelevel <= 0f)
            {
                PluginLog.Debug("vote hud skipped: changelevel deadline passed; changing now");
                LockWinner();
                ChangeToWinner();
                return;
            }

            bool openForVote = untilReveal > 0f;
            foreach (CCSPlayerController player in Humans.GetHumans())
            {
                VoteHud.OpenFor(player, captureMouse: openForVote);
            }

            _voteHudOpen = true;
            KillOpenTimers();

            if (!openForVote)
            {
                PluginLog.Debug("vote hud open: open window elapsed; starting reveal");
                BeginReveal();
                return;
            }

            _openTimer = AddTimer(untilReveal, BeginReveal);
            PluginLog.Debug(
                $"vote hud open: {untilReveal:0.###}s until reveal, {untilChangelevel:0.###}s until changelevel");
        }

        private void LockWinner()
        {
            if (_session is null)
            {
                return;
            }

            _session.Lock();
            string winner = _session.Winner ?? _session.Maps[0];
            _pendingWinner = winner;
            _pendingOrigin = _origins.GetValueOrDefault(winner, MapOrigin.Local);
        }

        /// <summary>
        /// Blank native end-match vote slots now and once more next frame so a late
        /// engine write cannot repopulate the Valve panel.
        /// </summary>
        private void SuppressNativeVoteSlots()
        {
            EngineVotePrep.ClearNativeVoteSlots();
            Server.NextFrame(() =>
            {
                if (_voteActive || _revealActive)
                {
                    EngineVotePrep.ClearNativeVoteSlots();
                }
            });
        }

        private List<MapEntry> SelectCandidates()
        {
            IReadOnlyList<MapEntry> catalog = _catalog.Maps;
            if (catalog.Count == 0)
            {
                return [];
            }

            string? exclude = Config.Endmatch.ExcludeCurrentMap
                ? Server.MapName.ToLowerInvariant()
                : null;
            List<MapEntry> maps = MapVisibility.Shown(catalog, Config.MapsVotable, exclude);
            if (maps.Count <= MapCatalog.MaxMaps)
            {
                return maps;
            }

            int original = maps.Count;
            maps.RemoveRange(MapCatalog.MaxMaps, maps.Count - MapCatalog.MaxMaps);
            PluginLog.Debug($"map list truncated from {original} to {MapCatalog.MaxMaps}");
            return maps;
        }

        private void BeginReveal()
        {
            if (!_voteActive || _revealActive || _session is null)
            {
                return;
            }

            KillOpenTimers();
            LockWinner();
            _revealActive = true;

            string winner = _pendingWinner!;
            VoteHud.BeginReveal(winner);

            float remaining = RemainingUntil(_timing.ChangelevelOffsetSeconds);
            PluginLog.Info($"map voting completed: winner={winner} ({_session.CountOf(winner)} votes)");
            PluginLog.Debug(
                $"vote locked: tally=[{string.Join(", ", _session.Maps.Select(m => $"{m}={_session.CountOf(m)}"))}] changelevel in {Math.Max(0f, remaining):0.###}s");

            _revealTimer?.Kill();
            _revealTimer = null;
            if (remaining <= 0f)
            {
                ChangeToWinner();
                return;
            }

            _revealTimer = AddTimer(remaining, ChangeToWinner);
        }

        private void ChangeToWinner()
        {
            string? winner = _pendingWinner;
            MapOrigin origin = _pendingOrigin;
            EndVoteUi();

            if (string.IsNullOrWhiteSpace(winner) || !MapNameFilter.IsValidMapName(winner))
            {
                PluginLog.Info($"changelevel refused: invalid winner '{winner}'");
                return;
            }

            _plays.Increment(winner);
            string command = EngineVotePrep.ChangelevelCommand(origin, winner);
            PluginLog.Info($"changing to map {winner}");
            PluginLog.Debug($"changelevel command={command} origin={origin}");
            Server.ExecuteCommand(command);
        }

        private void EndVoteUi()
        {
            KillOpenTimers();
            KillCatalogWaitTimer();
            _catalogPreparing = false;
            _revealTimer?.Kill();
            _revealTimer = null;
            _voteActive = false;
            _voteHudOpen = false;
            _revealActive = false;
            _session = null;
            _origins = new Dictionary<string, MapOrigin>(StringComparer.Ordinal);
            VoteHud.CloseAll();
        }

        private void KillOpenTimers()
        {
            _openTimer?.Kill();
            _openTimer = null;
        }

        private void KillCatalogWaitTimer()
        {
            _catalogWaitTimer?.Kill();
            _catalogWaitTimer = null;
        }

        private void OpenVoteHudForJoiner(CCSPlayerController player)
        {
            if (_session is null)
            {
                return;
            }

            if (_revealActive)
            {
                VoteHud.OpenFor(player, captureMouse: false);
                if (_pendingWinner is not null)
                {
                    VoteHud.PaintRevealFor(player, _pendingWinner);
                }

                return;
            }

            if (_voteHudOpen)
            {
                VoteHud.OpenFor(player);
            }
        }
    }
}
