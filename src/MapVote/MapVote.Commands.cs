using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Modules.Commands;
using MapVote.Utils;

namespace MapVote
{
    public partial class MapVote
    {
        [ConsoleCommand("css_rtv", "Rock the vote — force the match toward the map vote.")]
        [ConsoleCommand("rtv", "Rock the vote — force the match toward the map vote.")]
        [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
        public void OnRtvCommand(CCSPlayerController? player, CommandInfo info)
        {
            if (!Config.Enabled || !Config.Rtv.Enabled || player is null || !Players.IsHumanViewer(player))
            {
                return;
            }

            if (player.AuthorizedSteamID is null)
            {
                return;
            }

            ulong id = player.AuthorizedSteamID.SteamId64;
            if (!_rtvVoters.Add(id))
            {
                info.ReplyToCommand(Localizer["rtv.already"]);
                return;
            }

            int humans = Players.GetHumans().Count();
            int need = Math.Max(1, (int)Math.Ceiling(humans * Math.Clamp(Config.Rtv.Ratio, 0.01f, 1f)));
            int have = _rtvVoters.Count;
            Server.PrintToChatAll(string.Format(Localizer["rtv.progress"], have, need));

            if (have < need)
            {
                return;
            }

            _rtvVoters.Clear();
            Server.PrintToChatAll(Localizer["rtv.success"]);
            Server.ExecuteCommand(Config.Rtv.SuccessCommand);
        }
    }
}
