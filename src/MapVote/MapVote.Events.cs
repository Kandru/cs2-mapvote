using CounterStrikeSharp.API.Core;
using MapVote.Huds;
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
            }

            CustomHud.ReleasePlayer(player);
            return HookResult.Continue;
        }

        private void OnCustomHudClicked(CCSPlayerController player, CCSCustomHudLayout layout, string buttonId) =>
            _ = HudMenu.OnCustomHudClicked(player, layout, buttonId);
    }
}
