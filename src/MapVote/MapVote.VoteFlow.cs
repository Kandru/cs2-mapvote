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
        /// Win-panel entry: apply vote-time override, disable same-map restart, refresh the
        /// catalog during the match-results hold, decide custom vs native at 4s, open our HUD at 5s.
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
            _customPathChosen = false;

            EngineVotePrep.ApplyEndMatchOverrides(Config.Endmatch.VoteSeconds);
            EngineVotePrep.DisableMatchEndRestart();

            // Prefer config overrides for the schedule; ExecuteCommand may not update ConVar reads yet.
            _voteSeconds = Config.Endmatch.VoteSeconds is int voteOverride
                ? Math.Max(1, voteOverride)
                : VoteClock.ReadVoteSeconds(ConVars.ReadFloat);
            _timing = VoteClock.ResolveCustom(
                _voteSeconds,
                Config.Endmatch.WinnerRevealSeconds);

            int catalogTimeout = VoteClock.CatalogTimeoutSeconds();
            int decisionAt = VoteClock.DecisionOffsetSeconds;
            _catalogPreparing = true;
            _catalogGeneration = _catalog.Generation;

            PluginLog.Debug(
                $"win panel: vote={_voteSeconds}s catalog_timeout={catalogTimeout}s decision={decisionAt}s " +
                $"open≈{_timing.OpenSeconds}s reveal={_timing.RevealSeconds}s changelevel_at={_timing.ChangelevelOffsetSeconds}s " +
                $"generation={_catalogGeneration}");

            KillCatalogWaitTimer();
            KillPanelOpenTimer();
            _catalog.RequestRefresh(catalogTimeout);

            float untilDecision = RemainingUntil(decisionAt);
            if (untilDecision <= 0f)
            {
                OnVotePathDecision();
            }
            else
            {
                _catalogWaitTimer = AddTimer(untilDecision, OnVotePathDecision);
            }
        }

        /// <summary>Main-thread settle after RCON refresh. Does not open the HUD or blank slots.</summary>
        private void OnCatalogSettled(IReadOnlyList<MapEntry> maps, int generation)
        {
            if (_unloaded || generation != _catalogGeneration || !_catalogPreparing || _customPathChosen)
            {
                return;
            }

            SyncMapsVotable(maps);

            List<MapEntry> candidates = SelectCandidates();
            float queryElapsed = (float)(DateTime.UtcNow - _scheduleEpochUtc).TotalSeconds;
            if (candidates.Count == 0)
            {
                _pendingCandidates = null;
                PluginLog.Debug($"win-panel map list empty: query={queryElapsed:0.###}s");
                return;
            }

            _pendingCandidates = candidates;
            PluginLog.Debug(
                $"win-panel map list ready: query={queryElapsed:0.###}s maps={candidates.Count} " +
                $"open≈{_timing.OpenSeconds}s changelevel_at={_timing.ChangelevelOffsetSeconds}s");
        }

        /// <summary>
        /// At match-results − 1s: blank native slots for the custom path, or arm the Valve vote.
        /// </summary>
        private void OnVotePathDecision()
        {
            _catalogWaitTimer = null;
            if (_unloaded || !_catalogPreparing || _voteActive || _revealActive)
            {
                return;
            }

            if (_pendingCandidates is { Count: > 0 } candidates)
            {
                EngineVotePrep.ClearNativeVoteSlots();
                _customPathChosen = true;
                PluginLog.Debug(
                    $"vote path: custom ({candidates.Count} maps); blanked native slots");

                // No room after the match-results hold: open (or changelevel) immediately.
                if (_timing.ChangelevelOffsetSeconds <= VoteClock.MatchResultsSeconds)
                {
                    BeginVote(candidates);
                    return;
                }

                float untilOpen = RemainingUntil(VoteClock.MatchResultsSeconds);
                if (untilOpen <= 0f)
                {
                    OnMatchResultsElapsed();
                }
                else
                {
                    _panelOpenTimer = AddTimer(untilOpen, OnMatchResultsElapsed);
                }

                return;
            }

            EnableNativeVote("no candidates at decision deadline");
        }

        private void OnMatchResultsElapsed()
        {
            _panelOpenTimer = null;
            if (_unloaded || !_catalogPreparing || !_customPathChosen || _voteActive || _revealActive)
            {
                return;
            }

            if (_pendingCandidates is { Count: > 0 } candidates)
            {
                BeginVote(candidates);
                return;
            }

            EnableNativeVote("custom path lost candidates before menu open");
        }

        private void EnableNativeVote(string reason)
        {
            KillCatalogWaitTimer();
            KillPanelOpenTimer();
            _catalogPreparing = false;
            _customPathChosen = false;
            _pendingCandidates = null;
            EngineVotePrep.ArmNativeVote();
            PluginLog.Info($"vote skipped: {reason}; Valve end-match vote armed");
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
            _customPathChosen = false;
            _pendingCandidates = null;

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
        /// Opens the overlay after the match-results hold. Timers are pinned to the win-panel epoch
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
            _customPathChosen = false;
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
