using CounterStrikeSharp.API.Core;
using MapVote.Huds;
using MapVote.Players;
using MapVote.Utils;

namespace MapVote
{
    public partial class MapVote
    {
        private void OnMapStart(string mapName)
        {
            _rtvVoters.Clear();
            EndVoteUi();
            CustomHud.ResetRound();
            _catalog.RequestRefresh(force: true);
        }

        private void OnMapEnd()
        {
            EndVoteUi();
            CustomHud.Shutdown();
        }

        private HookResult OnMatchPoint(EventRoundAnnounceMatchPoint @event, GameEventInfo info)
        {
            if (Config.Enabled)
            {
                _catalog.RequestRefresh();
            }

            return HookResult.Continue;
        }

        private HookResult OnIntermission(EventCsIntermission @event, GameEventInfo info)
        {
            if (Config.Enabled && !_voteActive)
            {
                StartVote();
            }

            return HookResult.Continue;
        }

        private HookResult OnPlayerConnectFull(EventPlayerConnectFull @event, GameEventInfo info)
        {
            CCSPlayerController? player = @event.Userid;
            if (player is { IsValid: true } && player.AuthorizedSteamID is { SteamId64: > 0 } steam)
            {
                PlayerState state = _archive.GetOrCreate(steam.SteamId64.ToString());
                if (string.IsNullOrEmpty(state.Username) || state.Username == state.SteamId)
                {
                    state.Username = player.PlayerName ?? state.SteamId;
                }
            }

            return HookResult.Continue;
        }

        private HookResult OnRoundEnd(EventRoundEnd @event, GameEventInfo info)
        {
            if (!Config.Persistence.AutosaveOnRoundEnd)
            {
                return HookResult.Continue;
            }

            int queued = 0;
            foreach (PlayerState state in _archive.States)
            {
                if (!state.Dirty || state.SteamId.Length == 0)
                {
                    continue;
                }

                _saves.Enqueue(state);
                queued++;
            }

            if (queued > 0 && Config.Debug)
            {
                PluginLog.Info($"[players] round end: queued {queued} dirty player(s)");
            }

            return HookResult.Continue;
        }

        private HookResult OnPlayerDisconnect(EventPlayerDisconnect @event, GameEventInfo info)
        {
            CCSPlayerController? player = @event.Userid;
            if (player is null)
            {
                return HookResult.Continue;
            }

            if (player.AuthorizedSteamID is { } steam)
            {
                _rtvVoters.Remove(steam.SteamId64);
                if (_session is { IsOpen: true } && _session.RemovePlayer(steam.SteamId64))
                {
                    VoteHud.PaintAll();
                }

                if (_archive.TryGet(steam.SteamId64.ToString()) is { Dirty: true } state)
                {
                    _saves.Enqueue(state);
                }
            }

            CustomHud.ReleasePlayer(player);
            return HookResult.Continue;
        }

        private void OnCustomHudClicked(CCSPlayerController player, CCSCustomHudLayout layout, string buttonId) =>
            _ = HudMenu.OnCustomHudClicked(player, layout, buttonId);
    }
}
