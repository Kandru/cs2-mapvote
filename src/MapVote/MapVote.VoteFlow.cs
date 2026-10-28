using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using MapVote.Huds;
using MapVote.Maps;
using MapVote.Utils;
using PlayerUtil = MapVote.Utils.Players;
using MapVote.Voting;
using TimerFlags = CounterStrikeSharp.API.Modules.Timers.TimerFlags;

namespace MapVote
{
    public partial class MapVote
    {
        private const float CatalogWaitIntervalSeconds = 0.25f;
        private const int CatalogWaitMaxSeconds = 2;

        private bool VoteBusy => _voteActive || _revealActive || _catalogWaitTimer is not null;

        /// <summary>Seconds left until intermission epoch + <paramref name="offsetSeconds"/>.</summary>
        private float RemainingUntil(int offsetSeconds) =>
            VoteClock.SecondsUntil(_scheduleEpochUtc, offsetSeconds, DateTime.UtcNow);

        /// <summary>
        /// Snapshot end-match cvars before intermission. Prefer our overlay unless the
        /// catalog is known empty / all maps disabled (then enable the Valve vote).
        /// </summary>
        private void ArmEngineVote()
        {
            VoteTiming timing = VoteClock.Resolve(Config.Endmatch);
            if (_catalog.IsLoading || MapVisibility.AnyShown(_catalog.Maps, Config.MapsVotable))
            {
                EngineVotePrep.PrepareCustomVote(timing.EngineHoldSeconds);
                _nativeVoteArmed = false;
                PluginLog.Debug($"engine vote: custom hold={timing.EngineHoldSeconds}s");
                return;
            }

            if (_nativeVoteArmed)
            {
                return;
            }

            EngineVotePrep.PrepareNativeVote(timing.OpenSeconds);
            _nativeVoteArmed = true;
            PluginLog.Info($"catalog empty or no votable maps; enabling Valve end-match vote ({timing.OpenSeconds}s)");
        }

        private void StartVote()
        {
            if (VoteBusy)
            {
                PluginLog.Debug("vote start ignored: already active");
                return;
            }

            // Align plugin timers with the engine hold countdown (vote + reveal + 1s slack).
            _scheduleEpochUtc = DateTime.UtcNow;
            _timing = VoteClock.Resolve(Config.Endmatch);

            List<MapEntry> maps = SelectCandidates();
            if (maps.Count > 0)
            {
                BeginVote(maps);
                return;
            }

            if (!_catalog.IsLoading)
            {
                EnableNativeVote("catalog empty and not loading");
                return;
            }

            PluginLog.Debug("vote waiting for catalog refresh");
            _catalogWaitTimer = AddTimer(CatalogWaitIntervalSeconds, OnCatalogWaitTick, TimerFlags.REPEAT);
        }

        private void OnCatalogWaitTick()
        {
            if (_catalogWaitTimer is null)
            {
                return;
            }

            List<MapEntry> maps = SelectCandidates();
            if (maps.Count > 0)
            {
                KillCatalogWaitTimer();
                PluginLog.Debug("catalog wait succeeded");
                BeginVote(maps);
                return;
            }

            if (!_catalog.IsLoading || RemainingUntil(CatalogWaitMaxSeconds) <= 0f)
            {
                string reason = !_catalog.IsLoading
                    ? "catalog empty and not loading"
                    : "catalog still empty after wait";
                EnableNativeVote(reason);
            }
        }

        private void EnableNativeVote(string reason)
        {
            KillCatalogWaitTimer();
            VoteTiming timing = VoteClock.Resolve(Config.Endmatch);
            PluginLog.Info($"vote skipped: {reason}; enabling Valve end-match vote ({timing.OpenSeconds}s)");
            EngineVotePrep.PrepareNativeVote(timing.OpenSeconds);
            _nativeVoteArmed = true;
        }

        private void BeginVote(List<MapEntry> maps)
        {
            if (_voteActive || _revealActive)
            {
                return;
            }

            KillCatalogWaitTimer();
            ArmEngineVote();
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

            PluginLog.Info(
                $"vote armed: {maps.Count} maps, open={_timing.OpenSeconds}s reveal={_timing.RevealSeconds}s hold={_timing.EngineHoldSeconds}s (hud waits for win panel).");
        }

        /// <summary>
        /// Opens the overlay when the match win panel fires. Session setup already ran at intermission.
        /// Uses remaining time from the intermission epoch so changelevel stays at the start of the slack second.
        /// </summary>
        private void ShowVoteHud()
        {
            if (!_voteActive || _revealActive || _voteHudOpen || _session is null)
            {
                return;
            }

            foreach (CCSPlayerController player in PlayerUtil.GetHumans())
            {
                VoteHud.OpenFor(player);
            }

            _voteHudOpen = true;
            KillOpenTimers();

            float remainingOpen = RemainingUntil(_timing.OpenSeconds);
            if (remainingOpen <= 0f)
            {
                PluginLog.Info("vote hud open: open window elapsed; starting reveal");
                BeginReveal();
                return;
            }

            _openTimer = AddTimer(remainingOpen, BeginReveal);
            PluginLog.Info($"vote hud open: {remainingOpen:0.###}s until reveal");
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
            PluginLog.Info($"map list truncated from {original} to {MapCatalog.MaxMaps}.");
            return maps;
        }

        private void BeginReveal()
        {
            if (!_voteActive || _revealActive || _session is null)
            {
                return;
            }

            KillOpenTimers();
            _session.Lock();

            string winner = _session.Winner ?? _session.Maps[0];
            _pendingWinner = winner;
            _pendingOrigin = _origins.GetValueOrDefault(winner, MapOrigin.Local);
            _revealActive = true;

            VoteHud.BeginReveal(winner);

            float remaining = RemainingUntil(_timing.TotalSeconds);
            PluginLog.Info(
                $"vote locked: winner={winner} votes={_session.CountOf(winner)} changelevel in {Math.Max(0f, remaining):0.###}s");

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
            PluginLog.Info($"changing map: {command}");
            Server.ExecuteCommand(command);
        }

        private void EndVoteUi()
        {
            KillOpenTimers();
            KillCatalogWaitTimer();
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
