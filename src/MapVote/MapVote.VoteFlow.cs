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
                PluginLog.Debug("intermission ignored: end-match already running");
                return;
            }

            _pendingCandidates = null;
            _intermissionEpochUtc = DateTime.UtcNow;
            _catalogGeneration = _catalog.Generation;

            int budget = VoteClock.CatalogTimeoutSeconds();
            PluginLog.Debug(() =>
                $"intermission: catalog_timeout={budget}s decision={VoteClock.DecisionOffsetSeconds}s generation={_catalogGeneration}");

            KillDecisionTimer();
            _catalog.RequestRefresh(budget);

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
                _decisionTimer = AddTimer(untilDecision, OnDecisionDeadline);
            }
        }

        /// <summary>Main-thread settle. Decides immediately when the query beats the 2s deadline.</summary>
        private void OnCatalogSettled(IReadOnlyList<MapEntry> maps, int generation)
        {
            if (_unloaded || generation != _catalogGeneration || _flow.Phase != EndMatchPhase.Preparing)
            {
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
            PluginLog.Debug(() => $"catalog settled: query={elapsed:0.###}s maps={candidates.Count}");
            ApplyPath(choice);
        }

        /// <summary>Backstop at intermission + 2s. Uses whatever list is already in hand.</summary>
        private void OnDecisionDeadline()
        {
            _decisionTimer = null;
            if (_unloaded || _flow.Phase != EndMatchPhase.Preparing)
            {
                return;
            }

            List<MapEntry> candidates = SelectCandidates();
            if (!_flow.TryDecide(candidates.Count, out PathChoice choice))
            {
                return;
            }

            _pendingCandidates = candidates;
            PluginLog.Debug(() => $"decision deadline: maps={candidates.Count}");
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
                    PluginLog.Debug("win panel ignored");
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
            KillDecisionTimer();
            if (!choice.Custom)
            {
                _pendingCandidates = null;
                EngineVotePrep.ArmNativeVote();
                PluginLog.Info("vote skipped: no candidates; Valve end-match vote armed");
                return;
            }

            int engineHold = WriteEngineHold();
            SuppressNativeVoteSlots();
            int maps = _pendingCandidates?.Count ?? 0;
            PluginLog.Info($"custom vote: {maps} maps; native vote disabled; engine hold={engineHold}s");
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
            PluginLog.Debug(() => $"engine hold armed: {hold}s");
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
                $"win panel: vote={_voteSeconds}s open≈{_timing.OpenSeconds}s reveal={_timing.RevealSeconds}s changelevel_at={_timing.ChangelevelOffsetSeconds}s");

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
                _holdTimer = AddTimer(untilOpen, OnMatchResultsElapsed);
            }
        }

        private void OnMatchResultsElapsed()
        {
            _holdTimer = null;
            if (_unloaded || !_flow.TryBeginVoting())
            {
                return;
            }

            if (_pendingCandidates is not { Count: > 0 } candidates)
            {
                PluginLog.Info("custom vote had no maps at menu open");
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
                PluginLog.Info("custom vote had no maps at menu open");
                EndVoteUi();
                return;
            }

            _session = new VoteSession(unique.ConvertAll(static m => m.Name));
            _origins = new Dictionary<string, MapOrigin>(unique.Count, StringComparer.Ordinal);
            foreach (MapEntry map in unique)
            {
                _origins[map.Name] = map.Origin;
            }

            VoteHud.Bind(
                _session,
                Config.Filters,
                _archive,
                _plays,
                (player, key) => Localization.ForPlayerSafe(Localizer, player, key),
                _origins);
            _pendingWinner = null;

            PluginLog.Info($"map voting started ({unique.Count} maps)");
            PluginLog.Debug(() =>
                $"vote armed: open={_timing.OpenSeconds}s reveal={_timing.RevealSeconds}s changelevel_at={_timing.ChangelevelOffsetSeconds}s");

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

            if (!openForVote)
            {
                PluginLog.Debug("vote hud open: open window elapsed; starting reveal");
                BeginReveal();
                return;
            }

            _openTimer = AddTimer(untilReveal, BeginReveal);
            PluginLog.Debug(() =>
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
            EngineVotePrep.SetNextLevel(winner);
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
            PluginLog.Debug(() => $"map list truncated from {original} to {MapCatalog.MaxMaps}");
            return maps;
        }

        private static List<MapEntry> DistinctCandidates(List<MapEntry> maps)
        {
            HashSet<string> seen = new(StringComparer.Ordinal);
            List<MapEntry> unique = new(maps.Count);
            foreach (MapEntry map in maps)
            {
                if (!string.IsNullOrEmpty(map.Name) && seen.Add(map.Name))
                {
                    unique.Add(map);
                }
            }

            return unique;
        }

        private void BeginReveal()
        {
            _openTimer = null;
            if (_unloaded || _leaveMap || !_flow.TryBeginReveal() || _session is null)
            {
                return;
            }

            LockWinner();
            string winner = _pendingWinner ?? _session.Maps[0];
            VoteHud.BeginReveal(winner);

            float remaining = RemainingUntil(_timing.ChangelevelOffsetSeconds);
            PluginLog.Info($"map voting completed: winner={winner} ({_session.CountOf(winner)} votes)");
            PluginLog.Debug(() => $"changelevel in {Math.Max(0f, remaining):0.###}s");

            if (remaining <= 0f)
            {
                ChangeToWinner();
                return;
            }

            _revealTimer = AddTimer(remaining, ChangeToWinner);
        }

        private void ChangeToWinner()
        {
            _revealTimer = null;
            if (_unloaded || _leaveMap || !_flow.TryBeginChange())
            {
                return;
            }

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
            PluginLog.Debug(() => $"changelevel command={command} origin={origin}");
            Server.ExecuteCommand(command);
        }

        private void EndVoteUi()
        {
            KillDecisionTimer();
            KillHoldTimer();
            _openTimer?.Kill();
            _openTimer = null;
            _revealTimer?.Kill();
            _revealTimer = null;
            _flow.Reset();
            _pendingCandidates = null;
            _session = null;
            _origins = new Dictionary<string, MapOrigin>(StringComparer.Ordinal);
            VoteHud.CloseAll();
        }

        private void KillDecisionTimer()
        {
            _decisionTimer?.Kill();
            _decisionTimer = null;
        }

        private void KillHoldTimer()
        {
            _holdTimer?.Kill();
            _holdTimer = null;
        }

        private void OpenVoteHudForJoiner(CCSPlayerController player)
        {
            if (_session is null)
            {
                return;
            }

            if (_flow.Phase == EndMatchPhase.Revealing)
            {
                VoteHud.OpenFor(player, captureMouse: false);
                if (_pendingWinner is not null)
                {
                    VoteHud.PaintRevealFor(player, _pendingWinner);
                }

                return;
            }

            if (_flow.Phase == EndMatchPhase.Voting)
            {
                VoteHud.OpenFor(player);
            }
        }
    }
}
