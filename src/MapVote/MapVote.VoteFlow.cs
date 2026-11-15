using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using MapVote.Huds;
using MapVote.Maps;
using MapVote.Runtime;
using MapVote.Utils;
using MapVote.Voting;

namespace MapVote
{
    public partial class MapVote
    {
        private readonly EndMatchFlow _flow = new();

        private bool VoteBusy => _flow.BlocksRtv;

        private float RemainingUntil(int offsetSeconds) =>
            VoteClock.SecondsUntil(_scheduleEpochUtc, offsetSeconds, DateTime.UtcNow);

        /// <summary>
        /// Intermission: refresh the catalog for at most 1s and decide custom vs native by 2s.
        /// The win panel has not fired yet, so the engine has not built its vote UI.
        /// </summary>
        private void BeginFromIntermission()
        {
            if (!_flow.TryBegin())
            {
                PluginLog.Debug("end-match: intermission ignored; end-match flow already running");
                return;
            }

            _pendingCandidates = null;
            _intermissionEpochUtc = DateTime.UtcNow;
            _catalogGeneration = _catalog.Generation;
            // A stale changelevel watchdog must not reopen RTV/browser for the new flow.
            _changeWatchdogToken++;

            int budget = VoteClock.CatalogTimeoutSeconds();
            PluginLog.Debug(() =>
                $"end-match: intermission detected; catalog budget={budget}s decision={VoteClock.DecisionOffsetSeconds}s generation={_catalogGeneration}");

            _flowEpoch++;
            _catalog.RequestRefresh(
                budget,
                Config.Catalog.QueryLocalMaps,
                Config.Catalog.QueryMapGroup);

            float untilDecision = VoteClock.SecondsUntil(
                _intermissionEpochUtc,
                VoteClock.DecisionOffsetSeconds,
                DateTime.UtcNow);
            if (untilDecision <= 0f)
            {
                OnDecisionDeadline();
            }
            else
            {
                AddFlowTimer(untilDecision, OnDecisionDeadline);
            }
        }

        /// <summary>Main-thread settle. Decides immediately when the query beats the 2s deadline.</summary>
        private void OnCatalogSettled(IReadOnlyList<MapEntry> maps, int generation)
        {
            if (_unloaded || _leaveMap || generation != _catalogGeneration || _flow.Phase != EndMatchPhase.Preparing)
            {
                PluginLog.Debug(
                    $"end-match: catalog settle ignored (unloaded={_unloaded}, generation={generation}/{_catalogGeneration}, phase={_flow.Phase})");
                return;
            }

            SyncMapsVotable(maps);
            List<MapEntry> candidates = SelectCandidates();
            if (!_flow.TryDecide(candidates.Count, out PathChoice choice))
            {
                return;
            }

            _pendingCandidates = candidates;
            float elapsed = (float)(DateTime.UtcNow - _intermissionEpochUtc).TotalSeconds;
            PluginLog.Debug(
                () => $"end-match: catalog settled after {elapsed:0.###}s with {candidates.Count} candidate map(s)");
            ApplyPath(choice);
        }

        /// <summary>Backstop at intermission + 2s. Uses whatever list is already in hand.</summary>
        private void OnDecisionDeadline()
        {
            if (_unloaded || _flow.Phase != EndMatchPhase.Preparing)
            {
                PluginLog.Debug(
                    $"end-match: decision deadline ignored (unloaded={_unloaded}, phase={_flow.Phase})");
                return;
            }

            List<MapEntry> candidates = SelectCandidates();
            if (!_flow.TryDecide(candidates.Count, out PathChoice choice))
            {
                return;
            }

            _pendingCandidates = candidates;
            PluginLog.Debug($"end-match: decision deadline reached with {candidates.Count} candidate map(s)");
            ApplyPath(choice);
        }

        /// <summary>
        /// Win panel (~3s after intermission). Does not query RCON.
        /// If the 2s decision has not run yet, it runs first. The caller then empties the
        /// native option list when the custom vote is the one in use.
        /// </summary>
        private void BeginEndMatchFromWinPanel()
        {
            bool preparing = _flow.Phase == EndMatchPhase.Preparing;
            List<MapEntry>? selected = preparing ? SelectCandidates() : _pendingCandidates;
            int count = selected?.Count ?? 0;
            if (!_flow.TryBeginHold(count, out bool decidedNow, out PathChoice choice))
            {
                if (decidedNow)
                {
                    _pendingCandidates = selected;
                    ApplyPath(choice);
                }
                else if (_flow.Phase is not (EndMatchPhase.Native or EndMatchPhase.Idle))
                {
                    PluginLog.Debug("end-match: win panel ignored; no hold transition");
                }

                return;
            }

            if (decidedNow)
            {
                _pendingCandidates = selected;
                ApplyPath(choice);
            }

            StartHudClock();
        }

        private void ApplyPath(PathChoice choice)
        {
            if (!choice.Custom)
            {
                _pendingCandidates = null;
                EngineVotePrep.ArmNativeVote();
                PluginLog.Info("end-match: no usable maps for a custom vote; Valve vote enabled");
                return;
            }

            int engineHold = WriteEngineHold();
            SuppressNativeVoteSlots();
            int maps = _pendingCandidates?.Count ?? 0;
            PluginLog.Info($"end-match: custom vote selected with {maps} map(s); Valve vote disabled");
            PluginLog.Debug($"end-match: engine hold={engineHold}s, native slots cleared");
        }

        /// <summary>Menu length: config override, otherwise the server cvar captured before we stretch it.</summary>
        private int BaseVoteSeconds() =>
            Config.Endmatch.VoteSeconds is int configured
                ? Math.Max(1, configured)
                : _baseVoteSeconds ??= VoteClock.ReadVoteSeconds(ConVars.ReadFloat);

        /// <summary>Stretches the engine end-match timer past our changelevel and turns its vote off.</summary>
        private int WriteEngineHold()
        {
            int hold = VoteClock.EngineHoldFromIntermission(BaseVoteSeconds());
            EngineVotePrep.SuppressNativeVote(hold);
            return hold;
        }

        /// <summary>Map start and hot reload: the next intermission must already see the stretched timer.</summary>
        private void ArmEngineHold()
        {
            int hold = WriteEngineHold();
            PluginLog.Debug($"end-match: engine hold armed at {hold}s");
        }

        /// <summary>
        /// Blank native end-match vote slots now and once more next frame so a late
        /// engine write after intermission cannot repopulate the Valve panel.
        /// </summary>
        private void SuppressNativeVoteSlots()
        {
            EngineVotePrep.ClearNativeVoteSlots();
            Server.NextFrame(() =>
            {
                if (!_unloaded && _flow.IsCustomVote)
                {
                    EngineVotePrep.ClearNativeVoteSlots();
                }
            });
        }

        private void StartHudClock()
        {
            _scheduleEpochUtc = DateTime.UtcNow;
            _voteSeconds = BaseVoteSeconds();
            _timing = VoteClock.ResolveCustom(_voteSeconds, Config.Endmatch.WinnerRevealSeconds);

            PluginLog.Debug(() =>
                $"end-match: clock started vote={_voteSeconds}s results={VoteClock.MatchResultsSeconds}s "
                + $"open={_timing.OpenSeconds}s reveal={_timing.RevealSeconds}s changelevel_at={_timing.ChangelevelOffsetSeconds}s");

            if (_timing.ChangelevelOffsetSeconds <= VoteClock.MatchResultsSeconds)
            {
                OnMatchResultsElapsed();
                return;
            }

            float untilOpen = RemainingUntil(VoteClock.MatchResultsSeconds);
            if (untilOpen <= 0f)
            {
                OnMatchResultsElapsed();
            }
            else
            {
                AddFlowTimer(untilOpen, OnMatchResultsElapsed);
            }
        }

        private void OnMatchResultsElapsed()
        {
            if (_unloaded || !_flow.TryBeginVoting())
            {
                PluginLog.Debug(
                    $"end-match: match-results hold ignored (unloaded={_unloaded}, phase={_flow.Phase})");
                return;
            }

            if (_pendingCandidates is not { Count: > 0 } candidates)
            {
                PluginLog.Info("end-match: custom vote has no candidate maps at menu open");
                EndVoteUi();
                return;
            }

            BeginVote(candidates);
        }

        private void BeginVote(List<MapEntry> maps)
        {
            List<MapEntry> unique = DistinctCandidates(maps);
            if (unique.Count == 0)
            {
                PluginLog.Info("end-match: custom vote has no unique candidate maps");
                EndVoteUi();
                return;
            }

            _session = new VoteSession(unique.ConvertAll(static m => m.Name));
            int carried = Config.Nominations.Enabled && _nominationSession is { } nominations
                ? nominations.CopyVotesTo(_session)
                : 0;
            _origins = new Dictionary<string, MapOrigin>(unique.Count, StringComparer.Ordinal);
            foreach (MapEntry map in unique)
            {
                _origins[map.Name] = map.Origin;
            }

            var (currentMap, currentOrigin) = CurrentMap();
            VoteHud.Bind(
                _session,
                Config.Filters,
                _archive,
                _plays,
                (player, key) => Localization.ForPlayerSafe(Localizer, player, key),
                _origins,
                currentMap,
                currentOrigin,
                Config.CleanMapNames);
            _pendingWinner = null;

            int humans = Humans.GetHumans().Count();
            int shown = Math.Min(unique.Count, VoteHud.SlotCount);
            PluginLog.Info(
                $"map vote started: {unique.Count} map(s), {humans} player(s), {shown} shown, {carried} nomination(s) carried, {_voteSeconds}s");
            PluginLog.Debug(() =>
                $"map vote: open={_timing.OpenSeconds}s reveal={_timing.RevealSeconds}s changelevel_at={_timing.ChangelevelOffsetSeconds}s current={currentMap} ({currentOrigin})");

            ShowVoteHud();
        }

        /// <summary>
        /// Opens the overlay after the match-results hold. Timers are pinned to the win-panel epoch
        /// so <c>changelevel</c> stays at the vote length minus 1s.
        /// </summary>
        private void ShowVoteHud()
        {
            if (_flow.Phase != EndMatchPhase.Voting || _session is null)
            {
                return;
            }

            float untilReveal = RemainingUntil(_timing.RevealOffsetSeconds);
            float untilChangelevel = RemainingUntil(_timing.ChangelevelOffsetSeconds);

            if (untilChangelevel <= 0f)
            {
                PluginLog.Debug("map vote: changelevel deadline already passed; changing immediately");
                LockWinner();
                ChangeToWinner();
                return;
            }

            VoteHud.SetDeadline(_scheduleEpochUtc.AddSeconds(_timing.RevealOffsetSeconds));
            bool openForVote = untilReveal > 0f;
            foreach (CCSPlayerController player in Humans.GetHumans())
            {
                VoteHud.OpenFor(player, captureMouse: openForVote);
            }

            if (!openForVote)
            {
                PluginLog.Debug("map vote: voting window already elapsed; starting reveal");
                BeginReveal();
                return;
            }

            AddFlowTimer(untilReveal, BeginReveal);
            PluginLog.Debug(() =>
                $"map vote: HUD opened, reveal in {untilReveal:0.###}s, changelevel in {untilChangelevel:0.###}s");
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
            EngineVotePrep.SetNextLevel(winner);
        }

        /// <summary>Current map shown in the HUD. Resolved before current-map exclusion so it stays visible.</summary>
        private (string? Name, MapOrigin Origin) CurrentMap()
        {
            string raw = Server.MapName?.Trim() ?? string.Empty;
            if (raw.Length == 0)
            {
                return (null, MapOrigin.Local);
            }

            string name;
            MapOrigin origin;
            if (MapNameFilter.TryNormalizeMapReference(raw, out name, out origin))
            {
                if (origin == MapOrigin.Local
                    && IsWorkshopId(name)
                    && _catalog.TryGetWorkshopMapName(name, out string workshopName))
                {
                    name = workshopName;
                    origin = MapOrigin.Workshop;
                }
            }
            else
            {
                // Keep an unexpected current-map id visible even when it cannot be imaged.
                name = raw.ToLowerInvariant();
            }

            foreach (MapEntry map in _catalog.Maps)
            {
                if (string.Equals(map.Name, name, StringComparison.Ordinal))
                {
                    return (map.Name, map.Origin);
                }
            }

            return (name, origin);
        }

        private static bool IsWorkshopId(string mapName) =>
            mapName.Length > 0 && mapName.All(char.IsAsciiDigit);

        private List<MapEntry> SelectCandidates() =>
            SelectMaps(
                excludeCurrent: true,
                priority: Config.Nominations.Enabled ? _nominationSession?.VotedOrder() : null);

        /// <summary>
        /// Shown catalog maps in catalog order, capped at the HUD slot count.
        /// The browser keeps the current map; the vote path optionally excludes it.
        /// Every caller sees the same trimmed list, and priority names always survive the trim.
        /// </summary>
        private List<MapEntry> SelectMaps(bool excludeCurrent, IReadOnlyList<string>? priority = null)
        {
            IReadOnlyList<MapEntry> catalog = _catalog.Maps;
            if (catalog.Count == 0)
            {
                return [];
            }

            string? exclude = excludeCurrent && Config.Endmatch.ExcludeCurrentMap
                ? CurrentMap().Name
                : null;
            int limit = excludeCurrent
                ? Math.Clamp(Config.Endmatch.MaxMaps, 1, MapCatalog.MaxMaps)
                : MapCatalog.MaxMaps;
            List<MapEntry> selected = MapCandidateSelector.Select(
                catalog,
                Config.MapsVotable,
                exclude,
                limit,
                priority,
                static (found, capped) => PluginLog.Debug(
                    () => $"map candidates: {found} visible map(s) truncated to {capped}"));
            PluginLog.Debug(() =>
                $"map candidates: selected {selected.Count}/{catalog.Count} (exclude={exclude ?? "none"}, limit={limit}, "
                + $"priority={priority?.Count ?? 0}) [{string.Join(", ", selected.Select(static map => map.Name))}]");
            return selected;
        }

        private static List<MapEntry> DistinctCandidates(List<MapEntry> maps) =>
            MapCandidateSelector.Distinct(maps);

        private void BeginReveal()
        {
            if (_unloaded || _leaveMap || !_flow.TryBeginReveal() || _session is null)
            {
                PluginLog.Debug(
                    $"map vote: reveal ignored (unloaded={_unloaded}, leaving={_leaveMap}, phase={_flow.Phase}, session={_session is not null})");
                return;
            }

            LockWinner();
            string winner = _pendingWinner ?? _session.Maps[0];
            // Reveal first: the new deadline then starts the reveal's own timer panel.
            VoteHud.BeginReveal(winner);
            VoteHud.SetDeadline(_scheduleEpochUtc.AddSeconds(_timing.ChangelevelOffsetSeconds));

            float remaining = RemainingUntil(_timing.ChangelevelOffsetSeconds);
            PluginLog.Info($"map vote complete: winner {winner} with {_session.CountOf(winner)} vote(s)");
            PluginLog.Debug(() =>
            {
                string tally = string.Join(
                    ", ",
                    _session.VotedOrder().Select(map => $"{map}={_session.CountOf(map)}"));
                return $"map vote: final tally [{tally}], changelevel in {Math.Max(0f, remaining):0.###}s";
            });

            if (remaining <= 0f)
            {
                ChangeToWinner();
                return;
            }

            AddFlowTimer(remaining, ChangeToWinner);
        }

        private void ChangeToWinner()
        {
            if (_unloaded || _leaveMap || !_flow.TryBeginChange())
            {
                PluginLog.Debug(
                    $"changelevel: ignored (unloaded={_unloaded}, leaving={_leaveMap}, phase={_flow.Phase})");
                return;
            }

            string? winner = _pendingWinner;
            MapOrigin origin = _pendingOrigin;
            EndVoteUi();

            if (string.IsNullOrWhiteSpace(winner) || !MapNameFilter.IsValidMapName(winner))
            {
                PluginLog.Info($"changelevel: refused invalid winner '{winner}'");
                return;
            }

            _plays.Increment(winner);
            string command = EngineVotePrep.ChangelevelCommand(origin, winner);
            PluginLog.Info($"changing map to {winner} ({origin.ToString().ToLowerInvariant()})");
            PluginLog.Debug($"changelevel: command={command} origin={origin}");
            try
            {
                Server.ExecuteCommand(command);
            }
            catch (Exception ex)
            {
                PluginLog.Info($"changelevel: command failed for {winner}: {ex.Message}");
            }

            ArmChangeWatchdog(winner);
        }

        /// <summary>
        /// If no map end follows the changelevel, the next map never resets RTV and the
        /// browser, so recovery must not depend on a successful map transition.
        /// </summary>
        private void ArmChangeWatchdog(string winner)
        {
            int token = ++_changeWatchdogToken;
            AddTimer(GameConstants.ChangelevelWatchdogSeconds, () =>
            {
                if (token != _changeWatchdogToken || _unloaded || _leaveMap)
                {
                    return;
                }

                PluginLog.Info(
                    $"changelevel: map still '{Server.MapName}' {GameConstants.ChangelevelWatchdogSeconds}s after requesting {winner}; reopening RTV and !maps");
                OpenRtvForNewMatch("changelevel watchdog");
                OpenMapsForNewMatch("changelevel watchdog");
            });
        }

        private void EndVoteUi(bool closeHud = true)
        {
            PluginLog.Debug($"vote UI reset: epoch {_flowEpoch} -> {_flowEpoch + 1}, phase={_flow.Phase}");
            _flowEpoch++;
            _flow.Reset();
            _pendingCandidates = null;
            _session = null;
            _pendingWinner = null;
            _pendingOrigin = MapOrigin.Local;
            _origins = new Dictionary<string, MapOrigin>(StringComparer.Ordinal);
            if (closeHud)
            {
                VoteHud.CloseAll();
            }
            else
            {
                // Map end / unload: entities are destroyed by CustomHud.Shutdown.
                VoteHud.ResetView();
            }
        }

        /// <summary>One-shot flow timer; <see cref="EndVoteUi"/> cancels it by bumping the epoch.</summary>
        private void AddFlowTimer(float seconds, Action callback)
        {
            int epoch = _flowEpoch;
            AddTimer(seconds, () =>
            {
                if (epoch == _flowEpoch)
                {
                    callback();
                }
            });
        }

        /// <summary>Opens the vote HUD for a joiner a moment after connect, never in the connect tick.</summary>
        private void ScheduleVoteHudForJoiner(int slot, ulong steamId)
        {
            if (!PlayerSlots.IsValid(slot))
            {
                return;
            }

            int token = _hudOpenTokens.Next(slot);
            PluginLog.Debug($"map vote: scheduling HUD open for joiner slot={slot} in {JoinerOpenDelaySeconds}s");
            AddFlowTimer(JoinerOpenDelaySeconds, () =>
            {
                if (_unloaded
                    || _leaveMap
                    || !_hudOpenTokens.IsCurrent(slot, token)
                    || Utilities.GetPlayerFromSlot(slot) is not { IsValid: true } player
                    || !Humans.TryGetSteamId(player, out ulong liveSteamId)
                    || liveSteamId != steamId)
                {
                    PluginLog.Debug($"map vote: joiner HUD open skipped slot={slot} (stale token or player changed)");
                    return;
                }

                OpenVoteHudForJoiner(player);
            });
        }

        private void OpenVoteHudForJoiner(CCSPlayerController player)
        {
            if (_session is null)
            {
                return;
            }

            if (_flow.Phase == EndMatchPhase.Revealing)
            {
                PluginLog.Debug($"map vote: opening reveal HUD for joiner {Humans.Label(player)}");
                VoteHud.OpenFor(player, captureMouse: false);
                if (_pendingWinner is not null)
                {
                    VoteHud.PaintRevealFor(player, _pendingWinner);
                }

                return;
            }

            if (_flow.Phase == EndMatchPhase.Voting)
            {
                PluginLog.Debug($"map vote: opening vote HUD for joiner {Humans.Label(player)}");
                VoteHud.OpenFor(player);
            }
        }
    }
}
