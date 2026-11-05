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
        /// Optionally writes <c>mp_endmatch_votenextleveltime</c>. Null means leave the server cvar alone.
        /// Does not write <c>mp_match_restart_delay</c> or <c>mp_win_panel_display_time</c>.
        /// </summary>
        public static void ApplyEndMatchOverrides(int? voteSeconds)
        {
            if (voteSeconds is not null)
            {
                Server.ExecuteCommand($"mp_endmatch_votenextleveltime {Math.Max(1, voteSeconds.Value)}");
            }
        }

        /// <summary>
        /// Disables same-map restart so the end-match timer owns the post-match window.
        /// Does not arm the Valve vote UI.
        /// </summary>
        public static void DisableMatchEndRestart() =>
            Server.ExecuteCommand("mp_match_end_restart 0");

        /// <summary>
        /// Enables the Valve end-match vote and disables same-map restart.
        /// Does not write vote duration.
        /// </summary>
        public static void ArmNativeVote()
        {
            Server.ExecuteCommand("mp_endmatch_votenextmap 1");
            DisableMatchEndRestart();
        }

        /// <summary>
        /// Turns the Valve end-match vote cvar off. Does not touch the option list;
        /// the win panel overwrites that when our vote is the one in use.
        /// </summary>
        public static void SuppressNativeVote()
        {
            Server.ExecuteCommand("mp_endmatch_votenextmap 0");
            DisableMatchEndRestart();
        }

        /// <summary>Points a leftover engine level-change at our winner. The plugin still runs <c>changelevel</c>.</summary>
        public static void SetNextLevel(string map)
        {
            if (!MapNameFilter.IsValidMapName(map))
            {
                return;
            }

            Server.ExecuteCommand($"nextlevel {map}");
        }

        /// <summary>
        /// Writes <see cref="EmptyVoteSlot"/> into every native option slot so the Valve
        /// end-match panel has an empty list and skips itself.
        /// </summary>
        public static void ClearNativeVoteSlots()
        {
            CCSGameRules? rules = null;
            foreach (CCSGameRulesProxy proxy in Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules"))
            {
                if (proxy.IsValid)
                {
                    rules = proxy.GameRules;
                    break;
                }
            }

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
