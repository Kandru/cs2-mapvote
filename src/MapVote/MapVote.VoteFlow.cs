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
        private void StartVote()
        {
            if (_catalog.IsStale(TimeSpan.FromSeconds(Math.Max(1, Config.CatalogMaxAgeSeconds))))
            {
                _catalog.RequestRefresh(force: true);
            }

            List<MapEntry> maps = SelectCandidates();
            if (maps.Count == 0)
            {
                return;
            }

            _timing = VoteClock.Resolve(
                ConVars.ReadFloat("mp_endmatch_votenextleveltime"),
                ConVars.ReadInt("mp_match_restart_delay"),
                Config.FallbackVoteSeconds,
                Config.RevealSeconds);

            EngineVotePrep.Prepare(_timing.TotalSeconds);

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
                (player, key) => Localization.ForPlayerSafe(Localizer, player, key),
                state => _saves.Enqueue(state));
            _voteActive = true;
            _revealActive = false;
            _pendingWinner = null;

            foreach (CCSPlayerController player in Players.GetHumans())
            {
                VoteHud.OpenFor(player);
            }

            _openTimer?.Kill();
            _openTimer = AddTimer(_timing.OpenSeconds, () => BeginReveal(origins));
            PluginLog.Info($"vote started: {maps.Count} maps, open={_timing.OpenSeconds}s reveal={_timing.RevealSeconds}s.");
        }

        private List<MapEntry> SelectCandidates()
        {
            IReadOnlyList<MapEntry> source = _catalog.Maps;
            if (source.Count == 0)
            {
                PluginLog.Info("vote skipped: no maps loaded.");
                return [];
            }

            string current = Server.MapName.ToLowerInvariant();
            List<MapEntry> maps = source
                .Where(m => !Config.ExcludeCurrentMap || !string.Equals(m.Name, current, StringComparison.Ordinal))
                .Take(MapCatalog.MaxMaps)
                .ToList();

            if (maps.Count == 0)
            {
                PluginLog.Info("vote skipped: empty candidate list after filters.");
            }

            return maps;
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
            int votes = _session.CountOf(winner);
            _pendingWinner = winner;
            _pendingOrigin = origins.GetValueOrDefault(winner, MapOrigin.Local);
            _revealActive = true;

            VoteHud.BeginReveal(winner, votes);
            PluginLog.Info($"vote locked: winner={winner} votes={votes}");

            _revealTimer?.Kill();
            _revealTimer = AddTimer(_timing.RevealSeconds, ChangeToWinner);
        }

        private void ChangeToWinner()
        {
            string? winner = _pendingWinner;
            MapOrigin origin = _pendingOrigin;
            EndVoteUi();

            if (string.IsNullOrWhiteSpace(winner))
            {
                return;
            }

            _plays.Increment(Config, winner, warn: msg => PluginLog.Info($"[plays] {msg}"));
            string command = EngineVotePrep.ChangelevelCommand(origin, winner);
            PluginLog.Info($"changing map: {command}");
            AddTimer(0.5f, () => Server.ExecuteCommand(command));
        }

        private void EndVoteUi()
        {
            KillOpenTimers();
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
    }
}
