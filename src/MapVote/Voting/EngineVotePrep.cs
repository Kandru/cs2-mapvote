using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using MapVote.Maps;

namespace MapVote.Voting
{
    /// <summary>Configures Valve end-match vote cvars for our overlay or the native UI.</summary>
    public static class EngineVotePrep
    {
        public const int EmptyVoteSlot = -1;

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

        /// <summary>
        /// Blanks the networked end-match vote arrays so the Valve EOM voting panel
        /// sees no <c>voting_options</c> and skips itself.
        /// </summary>
        public static void ClearNativeVoteSlots()
        {
            CCSGameRules? rules = Utilities
                .FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules")
                .FirstOrDefault()
                ?.GameRules;
            if (rules is null)
            {
                return;
            }

            BlankVoteSlots(rules.EndMatchMapGroupVoteOptions);
            BlankVoteSlots(rules.EndMatchMapGroupVoteTypes);
        }

        /// <summary>Sets every slot to <see cref="EmptyVoteSlot"/>.</summary>
        internal static void BlankVoteSlots(Span<int> slots) => slots.Fill(EmptyVoteSlot);

        public static string ChangelevelCommand(MapOrigin origin, string map) =>
            origin == MapOrigin.Workshop
                ? $"ds_workshop_changelevel {map}"
                : $"changelevel {map}";
    }
}
