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
        /// Turns the Valve end-match vote off and stretches the engine timer past our changelevel.
        /// Does not blank the option list; the caller does that once the engine has finished writing it.
        /// </summary>
        public static void SuppressNativeVote(int engineHoldSeconds)
        {
            int hold = Math.Max(1, engineHoldSeconds);
            Server.ExecuteCommand("mp_endmatch_votenextmap 0");
            DisableMatchEndRestart();
            Server.ExecuteCommand($"mp_endmatch_votenextleveltime {hold}");
            Server.ExecuteCommand($"mp_match_restart_delay {hold}");
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
            CCSGameRulesProxy? proxy = Utilities
                .FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules")
                .FirstOrDefault(static candidate => candidate.IsValid);
            if (proxy?.GameRules is not CCSGameRules rules)
            {
                return;
            }

            BlankVoteSlots(rules.EndMatchMapGroupVoteOptions);
            BlankVoteSlots(rules.EndMatchMapGroupVoteTypes);
            // Span writes do not mark the rules dirty, so the client would keep the list
            // the engine already sent.
            Utilities.SetStateChanged(proxy, "CCSGameRulesProxy", "m_pGameRules");
        }

        /// <summary>Sets every slot to <see cref="EmptyVoteSlot"/>.</summary>
        internal static void BlankVoteSlots(Span<int> slots) => slots.Fill(EmptyVoteSlot);

        public static string ChangelevelCommand(MapOrigin origin, string map) =>
            origin == MapOrigin.Workshop
                ? $"ds_workshop_changelevel {map}"
                : $"changelevel {map}";
    }
}
