using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Core.Attributes.Registration;
using CounterStrikeSharp.API.Core.Translations;
using CounterStrikeSharp.API.Modules.Commands;
using MapVote.Utils;
using MapVote.Voting;

namespace MapVote
{
    public partial class MapVote
    {
        [ConsoleCommand("css_rtv", "Rock the vote - force the match toward the map vote.")]
        [ConsoleCommand("rtv", "Rock the vote - force the match toward the map vote.")]
        [CommandHelper(whoCanExecute: CommandUsage.CLIENT_ONLY)]
        public void OnRtvCommand(CCSPlayerController? player, CommandInfo info)
        {
            if (!Config.Rtv.Enabled || !Humans.TryGetSteamId(player, out ulong id))
            {
                PluginLog.Debug($"rtv ignored: enabled={Config.Rtv.Enabled}, player={Humans.Label(player)}");
                return;
            }

            string label = Humans.Label(player, id);

            if (_rtvClosed)
            {
                PluginLog.Info($"rtv: {label} requested but RTV is closed until next map");
                info.ReplyToCommand(Chat(Localizer["rtv.ended"]));
                return;
            }

            if (VoteBusy)
            {
                PluginLog.Info($"rtv: {label} requested but a vote is already running");
                info.ReplyToCommand(Chat(Localizer["rtv.closed"]));
                return;
            }

            if (_rtvPassed)
            {
                PluginLog.Debug($"rtv ignored: {label} already succeeded; success command pending");
                info.ReplyToCommand(Chat(Localizer["rtv.pending"]));
                return;
            }

            if (!_rtvVoters.Add(id))
            {
                PluginLog.Info($"rtv: {label} already voted ({_rtvVoters.Count} vote(s))");
                info.ReplyToCommand(Chat(Localizer["rtv.already"]));
                return;
            }

            int humans = CountRtvHumans();
            int need = RtvVotesNeeded(humans);
            int have = _rtvVoters.Count;
            PluginLog.Info($"rtv vote: {label} ({have}/{need} needed)");
            PluginLog.Debug($"rtv: humans={humans}, ratio={Config.Rtv.Ratio:0.##}, voters={have}, needed={need}");

            if (TrySucceedRtv(humans, label))
            {
                return;
            }

            Server.PrintToChatAll(Chat(Localizer["rtv.need", have, need]));
            if (have == 1)
            {
                StartRtvWindow();
            }
        }

        private int RtvVotesNeeded(int humanCount) =>
            RtvRule.VotesNeeded(humanCount, Config.Rtv.Ratio);

        /// <summary>
        /// Counts humans that can actually have an RTV vote (valid SteamID64), prunes
        /// votes from players who are no longer connected, and optionally ignores one id.
        /// </summary>
        private int CountRtvHumans(ulong? excludeSteamId = null)
        {
            HashSet<ulong> eligible = [];
            foreach (CCSPlayerController human in Humans.GetEligible())
            {
                if (Humans.TryGetSteamId(human, out ulong id)
                    && id != excludeSteamId)
                {
                    eligible.Add(id);
                }
            }

            int pruned = _rtvVoters.RemoveWhere(id => !eligible.Contains(id));
            if (pruned > 0)
            {
                PluginLog.Debug($"rtv: pruned {pruned} disconnected voter(s), {eligible.Count} eligible human(s)");
            }

            return eligible.Count;
        }

        /// <summary>
        /// Passes RTV when enough votes remain for the current human count.
        /// When <paramref name="deferCommand"/> is true, runs the success command next frame
        /// (needed from disconnect hooks).
        /// </summary>
        private bool TrySucceedRtv(int humanCount, string reason, bool deferCommand = false)
        {
            if (!Config.Rtv.Enabled || _rtvPassed || _rtvClosed || VoteBusy)
            {
                return false;
            }

            int have = _rtvVoters.Count;
            int need = RtvVotesNeeded(humanCount);
            if (have == 0 || have < need)
            {
                return false;
            }

            StopRtvWindow();
            _rtvVoters.Clear();
            _rtvPassed = true;
            Server.PrintToChatAll(Chat(Localizer["rtv.success", have, need]));
            PluginLog.Info($"rtv passed: {have}/{need} vote(s), reason={reason}; running '{Config.Rtv.SuccessCommand}'");
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

        private void StartRtvWindow()
        {
            StopRtvWindow();

            int token = _rtvToken;
            int duration = Math.Max(0, Config.Rtv.DurationSeconds);
            if (duration > 0)
            {
                _rtvDeadlineUtc = DateTime.UtcNow.AddSeconds(duration);
                AddTimer(duration, () => OnRtvExpired(token));
            }

            int announce = Math.Max(0, Config.Rtv.AnnounceSeconds);
            if (announce > 0)
            {
                ScheduleRtvAnnounce(token, announce);
            }

            PluginLog.Debug(
                $"rtv: window started token={token} duration={duration}s announce={announce}s");
        }

        /// <summary>Cancels the window timers by token; safe to call from their own callbacks.</summary>
        private void StopRtvWindow()
        {
            _rtvToken++;
            _rtvDeadlineUtc = null;
        }

        private void ScheduleRtvAnnounce(int token, int seconds) =>
            AddTimer(seconds, () => OnRtvAnnounce(token, seconds));

        private void OnRtvExpired(int token)
        {
            if (token != _rtvToken)
            {
                return;
            }

            if (_unloaded || _rtvPassed || _rtvClosed || _rtvVoters.Count == 0)
            {
                PluginLog.Debug($"rtv: expiry ignored token={token} unloaded={_unloaded} passed={_rtvPassed} closed={_rtvClosed} voters={_rtvVoters.Count}");
                StopRtvWindow();
                return;
            }

            int have = _rtvVoters.Count;
            int need = RtvVotesNeeded(CountRtvHumans());
            _rtvVoters.Clear();
            StopRtvWindow();
            PluginLog.Info($"rtv expired: {have}/{need} vote(s) were not enough");
            Server.PrintToChatAll(Chat(Localizer["rtv.expired"]));
        }

        private void OnRtvAnnounce(int token, int seconds)
        {
            if (token != _rtvToken)
            {
                return;
            }

            ScheduleRtvAnnounce(token, seconds);
            if (_unloaded || _rtvPassed || _rtvClosed || _rtvVoters.Count == 0)
            {
                return;
            }

            int humans = CountRtvHumans();
            int need = RtvVotesNeeded(humans);
            int have = _rtvVoters.Count;
            if (_rtvDeadlineUtc is DateTime deadline)
            {
                int left = Math.Max(0, (int)Math.Ceiling((deadline - DateTime.UtcNow).TotalSeconds));
                PluginLog.Debug($"rtv: reminder {have}/{need}, {left}s left, {humans} human(s)");
                Server.PrintToChatAll(Chat(Localizer["rtv.running", have, need, left]));
                return;
            }

            PluginLog.Debug($"rtv: reminder {have}/{need}, no deadline, {humans} human(s)");
            Server.PrintToChatAll(Chat(Localizer["rtv.need", have, need]));
        }

        /// <summary>
        /// Clears an open RTV window and blocks further votes until the next map
        /// or the next warmup / match on this map.
        /// </summary>
        private void CloseRtvForMatchEnd()
        {
            _rtvVoters.Clear();
            StopRtvWindow();
            _rtvClosed = true;
            PluginLog.Debug("rtv: closed until next match");
        }

        /// <summary>Map start always clears RTV. Warmup and match start do too, once our vote is gone.</summary>
        private void ResetRtv()
        {
            StopRtvWindow();
            _rtvVoters.Clear();
            _rtvPassed = false;
            _rtvClosed = false;
            PluginLog.Debug("rtv: reset for new map");
        }

        private void OpenRtvForNewMatch(string reason)
        {
            if (!_rtvClosed || !_flow.TryFinishForNewMatch())
            {
                return;
            }

            ResetRtv();
            PluginLog.Debug($"rtv: opened ({reason})");
        }

        private void ExecuteRtvSuccessCommand() =>
            Server.ExecuteCommand(Config.Rtv.SuccessCommand);

        private static string Chat(string message) =>
            ("{green}[MAPVOTE]{default} " + message).ReplaceColorTags();
    }
}
