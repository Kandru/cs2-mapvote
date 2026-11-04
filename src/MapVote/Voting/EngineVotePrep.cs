using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using MapVote.Maps;

namespace MapVote.Voting
{
    /// <summary>Configures Valve end-match vote cvars for our overlay or the native UI.</summary>
    public static class EngineVotePrep
    {
        public const int EmptyVoteSlot = -1;

        /// <summary>
        /// Optionally writes <c>mp_win_panel_display_time</c> and
        /// <c>mp_endmatch_votenextleveltime</c>. Null means leave the server cvar alone.
        /// Does not write <c>mp_match_restart_delay</c>.
        /// </summary>
        public static void ApplyEndMatchOverrides(int? voteSeconds, float? winPanelDisplayTime)
        {
            if (winPanelDisplayTime is not null)
            {
                Server.ExecuteCommand($"mp_win_panel_display_time {winPanelDisplayTime.Value}");
            }

            if (voteSeconds is not null)
            {
                Server.ExecuteCommand($"mp_endmatch_votenextleveltime {Math.Max(1, voteSeconds.Value)}");
            }
        }

        /// <summary>
        /// Enables the Valve end-match vote and disables same-map restart so the end-match
        /// timer owns the post-panel window. Does not write vote duration or the win panel.
        /// </summary>
        public static void ArmNativeVote()
        {
            Server.ExecuteCommand("mp_endmatch_votenextmap 1");
            Server.ExecuteCommand("mp_match_end_restart 0");
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
