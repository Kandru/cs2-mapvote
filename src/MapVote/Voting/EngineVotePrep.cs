using CounterStrikeSharp.API;
using MapVote.Maps;
using MapVote.Utils;

namespace MapVote.Voting
{
    /// <summary>Disables Valve's end-match vote UI and keeps the restart delay long enough for our overlay.</summary>
    public static class EngineVotePrep
    {
        public static void Prepare(int totalSeconds)
        {
            Server.ExecuteCommand("mp_endmatch_votenextmap 0");
            Server.ExecuteCommand("mp_match_end_restart 0");
            ConVars.EnsureMinInt("mp_match_restart_delay", totalSeconds);
        }

        public static string ChangelevelCommand(MapOrigin origin, string map) =>
            origin == MapOrigin.Workshop
                ? $"ds_workshop_changelevel {map}"
                : $"changelevel {map}";
    }
}
