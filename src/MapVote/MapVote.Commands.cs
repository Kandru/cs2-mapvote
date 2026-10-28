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
                PluginLog.Debug("rtv ignored: disabled or invalid player");
                return;
            }

            string label = PlayerUtil.Label(player, id);

            if (VoteBusy)
            {
                PluginLog.Info($"rtv failed: {label} — vote already running");
                info.ReplyToCommand(Chat(Localizer["rtv.closed"]));
                return;
            }

            if (_rtvPassed)
            {
                PluginLog.Debug($"rtv ignored: {label} — already succeeded");
                info.ReplyToCommand(Chat(Localizer["rtv.success"]));
                return;
            }

            if (!_rtvVoters.Add(id))
            {
                PluginLog.Info($"rtv failed: {label} — already voted");
                info.ReplyToCommand(Chat(Localizer["rtv.already"]));
                return;
            }

            int need = Math.Max(1, (int)Math.Ceiling(PlayerUtil.GetHumans().Count() * Math.Clamp(Config.Rtv.Ratio, 0.01f, 1f)));
            int have = _rtvVoters.Count;
            PluginLog.Info($"rtv invoked: {label} ({have}/{need})");

            if (have < need)
            {
                PluginLog.Info($"rtv failed: {label} — need {need}, have {have}");
                Server.PrintToChatAll(Chat(Localizer["rtv.need", have, need]));
                return;
            }

            _rtvVoters.Clear();
            _rtvPassed = true;
            Server.PrintToChatAll(Chat(Localizer["rtv.success"]));
            ArmEngineVote();
            PluginLog.Info($"rtv succeeded: {label} → {Config.Rtv.SuccessCommand}");
            Server.ExecuteCommand(Config.Rtv.SuccessCommand);
        }

        private static string Chat(string message) =>
            ("{green}[MAPVOTE]{default} " + message).ReplaceColorTags();
    }
}
