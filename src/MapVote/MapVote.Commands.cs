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
        [ConsoleCommand("css_rtv", "Rock the vote - force the match toward the map vote.")]
        [ConsoleCommand("rtv", "Rock the vote - force the match toward the map vote.")]
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
                PluginLog.Info($"rtv failed: {label} - vote already running");
                info.ReplyToCommand(Chat(Localizer["rtv.closed"]));
                return;
            }

            if (_rtvPassed)
            {
                PluginLog.Debug($"rtv ignored: {label} - already succeeded");
                info.ReplyToCommand(Chat(Localizer["rtv.pending"]));
                return;
            }

            if (!_rtvVoters.Add(id))
            {
                PluginLog.Info($"rtv failed: {label} - already voted");
                info.ReplyToCommand(Chat(Localizer["rtv.already"]));
                return;
            }

            int humans = CountRtvHumans();
            int need = RtvVotesNeeded(humans);
            int have = _rtvVoters.Count;
            PluginLog.Info($"rtv invoked: {label} ({have}/{need})");

            if (TrySucceedRtv(humans, label))
            {
                return;
            }

            PluginLog.Info($"rtv open: {label} - {have}/{need}");
            Server.PrintToChatAll(Chat(Localizer["rtv.need", have, need]));
        }

        private int RtvVotesNeeded(int humanCount) =>
            Math.Max(1, (int)Math.Ceiling(humanCount * Math.Clamp(Config.Rtv.Ratio, 0.01f, 1f)));

        private int CountRtvHumans(ulong? excludeSteamId = null)
        {
            int count = 0;
            foreach (CCSPlayerController human in PlayerUtil.GetHumans())
            {
                if (excludeSteamId is ulong id
                    && human.AuthorizedSteamID?.SteamId64 == id)
                {
                    continue;
                }

                count++;
            }

            return count;
        }

        /// <summary>
        /// Passes RTV when enough votes remain for the current human count.
        /// When <paramref name="deferCommand"/> is true, runs the success command next frame
        /// (needed from disconnect hooks).
        /// </summary>
        private bool TrySucceedRtv(int humanCount, string reason, bool deferCommand = false)
        {
            if (!Config.Rtv.Enabled || _rtvPassed || VoteBusy)
            {
                return false;
            }

            int have = _rtvVoters.Count;
            int need = RtvVotesNeeded(humanCount);
            if (have == 0 || have < need)
            {
                return false;
            }

            _rtvVoters.Clear();
            _rtvPassed = true;
            Server.PrintToChatAll(Chat(Localizer["rtv.success", have, need]));
            ArmEngineVote();
            PluginLog.Info($"rtv succeeded: {reason} → {Config.Rtv.SuccessCommand}");
            if (deferCommand)
            {
                Server.NextFrame(ExecuteRtvSuccessCommand);
            }
            else
            {
                ExecuteRtvSuccessCommand();
            }

            return true;
        }

        private void ExecuteRtvSuccessCommand() =>
            Server.ExecuteCommand(Config.Rtv.SuccessCommand);

        private static string Chat(string message) =>
            ("{green}[MAPVOTE]{default} " + message).ReplaceColorTags();
    }
}
