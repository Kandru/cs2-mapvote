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
        /// When every catalog map is disabled, arm the Valve vote early.
        /// Otherwise leave cvars alone until the win panel (do not disable native vote).
        /// Does not write <c>mp_match_restart_delay</c>.
        /// </summary>
        private void ArmEngineVote()
        {
            if (_catalog.Maps.Count == 0
                || _catalog.IsLoading
                || MapVisibility.AnyShown(_catalog.Maps, Config.MapsVotable))
            {
                _nativeVoteArmed = false;
                PluginLog.Debug("engine vote: defer to win panel");
                return;
            }

            if (_nativeVoteArmed)
            {
                return;
            }

            EngineVotePrep.ArmNativeVote();
            _nativeVoteArmed = true;
            PluginLog.Info("no votable maps; enabling Valve end-match vote");
        }

        /// <summary>
        /// Win-panel entry: apply overrides, arm the Valve vote, refresh the catalog during
        /// the panel, then open our HUD after <c>mp_win_panel_display_time</c> when maps are ready.
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
            _pendingCandidates = null;
            _panelElapsed = false;

            EngineVotePrep.ApplyEndMatchOverrides(
                Config.Endmatch.VoteSeconds,
                Config.Endmatch.WinPanelDisplayTime);
            EngineVotePrep.ArmNativeVote();
            _nativeVoteArmed = true;

            // Prefer config overrides for the schedule; ExecuteCommand may not update ConVar reads yet.
            _panelSeconds = Config.Endmatch.WinPanelDisplayTime is float panelOverride
                ? Math.Max(0, (int)Math.Ceiling(panelOverride))
                : VoteClock.ReadPanelSeconds(ConVars.ReadFloat);
            _voteSeconds = Config.Endmatch.VoteSeconds is int voteOverride
                ? Math.Max(1, voteOverride)
                : VoteClock.ReadVoteSeconds(ConVars.ReadFloat);
            _timing = VoteClock.ResolveCustom(
                _panelSeconds,
                _voteSeconds,
                Config.Endmatch.WinnerRevealSeconds);

            int catalogTimeout = VoteClock.CatalogTimeoutSeconds(_panelSeconds);
            _catalogPreparing = true;
            _catalogGeneration = _catalog.Generation;

            PluginLog.Debug(
                $"win panel: panel={_panelSeconds}s vote={_voteSeconds}s catalog_timeout={catalogTimeout}s " +
                $"open≈{_timing.OpenSeconds}s reveal={_timing.RevealSeconds}s changelevel_at={_timing.ChangelevelOffsetSeconds}s " +
                $"generation={_catalogGeneration}");

            KillCatalogWaitTimer();
            KillPanelOpenTimer();
            _catalogWaitTimer = AddTimer(Math.Max(0.25f, catalogTimeout + 0.5f), OnCatalogWatchdog);
            _catalog.RequestRefresh(catalogTimeout);

            float untilPanel = RemainingUntil(_panelSeconds);
            if (untilPanel <= 0f)
            {
                OnPanelElapsed();
            }
            else
            {
                _panelOpenTimer = AddTimer(untilPanel, OnPanelElapsed);
            }
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
            SyncMapsVotable(maps);

            List<MapEntry> candidates = SelectCandidates();
            if (candidates.Count == 0)
            {
                EnableNativeVote("catalog empty after win-panel refresh");
                return;
            }

            float queryElapsed = (float)(DateTime.UtcNow - _scheduleEpochUtc).TotalSeconds;
            _pendingCandidates = candidates;
            PluginLog.Debug(
                $"win-panel map list ready: query={queryElapsed:0.###}s maps={candidates.Count} " +
                $"panel_elapsed={_panelElapsed} open≈{_timing.OpenSeconds}s changelevel_at={_timing.ChangelevelOffsetSeconds}s");

            if (_panelElapsed)
            {
                BeginVote(candidates);
            }
        }

        private void OnPanelElapsed()
        {
            _panelOpenTimer = null;
            if (_unloaded || !_catalogPreparing || _voteActive || _revealActive)
            {
                return;
            }

            _panelElapsed = true;

            if (_pendingCandidates is { Count: > 0 } candidates)
            {
                BeginVote(candidates);
                return;
            }

            // Panel is only the earliest open time. While RCON is still running, keep waiting;
            // settle / watchdog decides custom vs leaving the already-armed native vote.
            if (_catalog.IsLoading || _catalogWaitTimer is not null)
            {
                PluginLog.Debug("win panel elapsed; waiting for catalog");
                return;
            }

            EnableNativeVote("no candidates when win panel elapsed");
        }

        private void EnableNativeVote(string reason)
        {
            KillCatalogWaitTimer();
            KillPanelOpenTimer();
            _catalogPreparing = false;
            _pendingCandidates = null;
            PluginLog.Info($"vote skipped: {reason}; leaving Valve end-match vote armed");
            _nativeVoteArmed = true;
        }

        private void BeginVote(List<MapEntry> maps)
        {
            if (_voteActive || _revealActive)
            {
                return;
            }

            KillCatalogWaitTimer();
            KillPanelOpenTimer();
            _catalogPreparing = false;
            _pendingCandidates = null;
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
        /// Opens the overlay after the win panel. Timers are pinned to the win-panel epoch
        /// so <c>changelevel</c> stays at <c>mp_endmatch_votenextleveltime</c> − 1s.
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
            KillPanelOpenTimer();
            _catalogPreparing = false;
            _panelElapsed = false;
            _pendingCandidates = null;
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

        private void KillPanelOpenTimer()
        {
            _panelOpenTimer?.Kill();
            _panelOpenTimer = null;
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
