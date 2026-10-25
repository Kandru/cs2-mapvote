using CounterStrikeSharp.API;
using MapVote.Maps;

namespace MapVote.Voting
{
    /// <summary>Configures Valve end-match vote cvars for our overlay or the native UI.</summary>
    public static class EngineVotePrep
    {
        public static void PrepareCustomVote(int engineHoldSeconds)
        {
            Server.ExecuteCommand("mp_endmatch_votenextmap 0");
            Server.ExecuteCommand("mp_match_end_restart 0");
            Server.ExecuteCommand($"mp_endmatch_votenextleveltime {engineHoldSeconds}");
            Server.ExecuteCommand($"mp_match_restart_delay {engineHoldSeconds}");
        }

        public static void PrepareNativeVote(int voteSeconds)
        {
            Server.ExecuteCommand("mp_endmatch_votenextmap 1");
            Server.ExecuteCommand($"mp_endmatch_votenextleveltime {voteSeconds}");
            Server.ExecuteCommand($"mp_match_restart_delay {voteSeconds}");
        }

        public static string ChangelevelCommand(MapOrigin origin, string map) =>
            origin == MapOrigin.Workshop
                ? $"ds_workshop_changelevel {map}"
                : $"changelevel {map}";
    }
}
