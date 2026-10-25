using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Core.Translations;
using CounterStrikeSharp.API.Modules.Commands;
using MapVote.Utils;
using PlayerUtil = MapVote.Utils.Players;

namespace MapVote
{
    public partial class MapVote
    {
        [ConsoleCommand("css_rtv", "Rock the vote — force the match toward the map vote.")]
        [ConsoleCommand("rtv", "Rock the vote — force the match toward the map vote.")]
        [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
        public void OnRtvCommand(CCSPlayerController? player, CommandInfo info)
        {
            if (!Config.Rtv.Enabled || player is null || !PlayerUtil.IsHumanViewer(player)
                || player.AuthorizedSteamID is not { SteamId64: var id })
            {
                return;
            }

            if (VoteBusy)
            {
                info.ReplyToCommand(Chat(Localizer["rtv.closed"]));
                return;
            }

            if (_rtvPassed)
            {
                info.ReplyToCommand(Chat(Localizer["rtv.success"]));
                return;
            }

            if (!_rtvVoters.Add(id))
            {
                info.ReplyToCommand(Chat(Localizer["rtv.already"]));
                return;
            }

            int need = Math.Max(1, (int)Math.Ceiling(PlayerUtil.GetHumans().Count() * Math.Clamp(Config.Rtv.Ratio, 0.01f, 1f)));
            int have = _rtvVoters.Count;
            PluginLog.Debug($"rtv {have}/{need}");

            if (have < need)
            {
                Server.PrintToChatAll(Chat(Localizer["rtv.need", have, need]));
                return;
            }

            _rtvVoters.Clear();
            _rtvPassed = true;
            Server.PrintToChatAll(Chat(Localizer["rtv.success"]));
            ArmEngineVote();
            PluginLog.Debug($"rtv success → {Config.Rtv.SuccessCommand}");
            Server.ExecuteCommand(Config.Rtv.SuccessCommand);
        }

        private static string Chat(string message) =>
            ("{green}[MAPVOTE]{default} " + message).ReplaceColorTags();
    }
}
