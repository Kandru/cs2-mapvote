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
        private const float CatalogWaitMaxSeconds = 2f;
        private const float ChangelevelDelaySeconds = 0.5f;

        private bool VoteBusy => _voteActive || _revealActive || _catalogWaitTimer is not null;

        /// <summary>
        /// Snapshot end-match cvars before intermission. Prefer our overlay unless the
        /// catalog is known empty (then enable the Valve vote).
        /// </summary>
        private void ArmEngineVote()
        {
            VoteTiming timing = VoteClock.Resolve(Config.Endmatch);
            if (_catalog.IsLoading || _catalog.Maps.Count > 0)
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
            PluginLog.Info($"catalog empty; enabling Valve end-match vote ({timing.OpenSeconds}s)");
        }

        private void StartVote()
        {
            if (VoteBusy)
            {
                PluginLog.Debug("vote start ignored: already active");
                return;
            }

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

            _catalogWaitDeadlineUtc = DateTime.UtcNow.AddSeconds(CatalogWaitMaxSeconds);
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

            if (!_catalog.IsLoading || DateTime.UtcNow >= _catalogWaitDeadlineUtc)
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

            _timing = VoteClock.Resolve(Config.Endmatch);
            ArmEngineVote();
            SuppressNativeVoteSlots();

            _session = new VoteSession(maps.Select(static m => m.Name).ToList());
            Dictionary<string, MapOrigin> origins = maps.ToDictionary(
                static m => m.Name,
                static m => m.Origin,
                StringComparer.Ordinal);

            VoteHud.Bind(
                _session,
                Config.Filters,
                _archive,
                _plays,
                (player, key) => Localization.ForPlayerSafe(Localizer, player, key));
            _voteActive = true;
            _revealActive = false;
            _pendingWinner = null;

            foreach (CCSPlayerController player in PlayerUtil.GetHumans())
            {
                VoteHud.OpenFor(player);
            }

            _openTimer?.Kill();
            _openTimer = AddTimer(_timing.OpenSeconds, () => BeginReveal(origins));
            PluginLog.Info($"vote started: {maps.Count} maps, open={_timing.OpenSeconds}s reveal={_timing.RevealSeconds}s.");
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
            IReadOnlyList<MapEntry> source = _catalog.Maps;
            if (source.Count == 0)
            {
                return [];
            }

            if (!Config.Endmatch.ExcludeCurrentMap)
            {
                return source.ToList();
            }

            string current = Server.MapName.ToLowerInvariant();
            return source
                .Where(m => !string.Equals(m.Name, current, StringComparison.Ordinal))
                .ToList();
        }

        private void BeginReveal(Dictionary<string, MapOrigin> origins)
        {
            if (!_voteActive || _revealActive || _session is null)
            {
                return;
            }

            KillOpenTimers();
            _session.Lock();

            string winner = _session.Winner ?? _session.Maps[0];
            _pendingWinner = winner;
            _pendingOrigin = origins.GetValueOrDefault(winner, MapOrigin.Local);
            _revealActive = true;

            VoteHud.BeginReveal(winner);
            PluginLog.Info($"vote locked: winner={winner} votes={_session.CountOf(winner)}");

            _revealTimer?.Kill();
            _revealTimer = AddTimer(_timing.RevealSeconds, ChangeToWinner);
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
            int generation = ++_changelevelGeneration;
            _changelevelTimer?.Kill();
            _changelevelTimer = AddTimer(ChangelevelDelaySeconds, () =>
            {
                if (generation != _changelevelGeneration)
                {
                    return;
                }

                _changelevelTimer = null;
                Server.ExecuteCommand(command);
            });
        }

        private void EndVoteUi()
        {
            KillOpenTimers();
            KillCatalogWaitTimer();
            KillChangelevelTimer();
            _revealTimer?.Kill();
            _revealTimer = null;
            _voteActive = false;
            _revealActive = false;
            _session = null;
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

        private void KillChangelevelTimer()
        {
            _changelevelTimer?.Kill();
            _changelevelTimer = null;
            _changelevelGeneration++;
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

            if (_voteActive)
            {
                VoteHud.OpenFor(player);
            }
        }
    }
}
