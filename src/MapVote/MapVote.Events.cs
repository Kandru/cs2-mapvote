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
            _rtvPassed = false;
            _steamBySlot.Clear();
            EndVoteUi();
            CustomHud.ResetRound();
            LoadPersistence();
            _catalog.RequestRefresh();
            PluginLog.Debug($"map start {mapName}");
        }

        private void OnMapEnd()
        {
            EndVoteUi();
            SavePersistence();
            CustomHud.Shutdown();
        }

        private HookResult OnIntermission(EventCsIntermission @event, GameEventInfo info)
        {
            if (!_voteActive && !_revealActive && _catalogWaitTimer is null)
            {
                PluginLog.Debug("intermission: starting vote");
                StartVote();
            }

            return HookResult.Continue;
        }

        private HookResult OnPlayerConnectFull(EventPlayerConnectFull @event, GameEventInfo info)
        {
            CCSPlayerController? player = @event.Userid;
            if (player is not { IsValid: true })
            {
                return HookResult.Continue;
            }

            if (player.AuthorizedSteamID is { SteamId64: > 0 } steam)
            {
                if (player.Slot is >= 0)
                {
                    _steamBySlot[player.Slot] = steam.SteamId64;
                }

                PlayerState state = _archive.GetOrCreate(steam.SteamId64.ToString());
                if (string.IsNullOrEmpty(state.Username) || state.Username == state.SteamId)
                {
                    state.Username = player.PlayerName ?? state.SteamId;
                }

                PluginLog.Debug($"connect: {state.Username} ({steam.SteamId64}) slot={player.Slot}");
            }

            if (_voteActive || _revealActive)
            {
                OpenVoteHudForJoiner(player);
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

            ulong? steamId = player.AuthorizedSteamID?.SteamId64;
            if (steamId is null or 0
                && player.Slot is >= 0
                && _steamBySlot.TryGetValue(player.Slot, out ulong cached))
            {
                steamId = cached;
            }

            if (player.Slot is >= 0)
            {
                _steamBySlot.Remove(player.Slot);
                VoteHud.ReleaseSlot(player.Slot);
            }

            if (steamId is > 0)
            {
                _rtvVoters.Remove(steamId.Value);
                if (_session is { IsOpen: true } && _session.RemovePlayer(steamId.Value))
                {
                    VoteHud.PaintAll();
                }

                PluginLog.Debug($"disconnect: {steamId} slot={player.Slot}");
            }

            CustomHud.ReleasePlayer(player);
            return HookResult.Continue;
        }

        private void OnCustomHudClicked(CCSPlayerController player, CCSCustomHudLayout layout, string buttonId) =>
            _ = HudMenu.OnCustomHudClicked(player, layout, buttonId);
    }
}
