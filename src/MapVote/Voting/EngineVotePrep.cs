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

        private static CCSGameRulesProxy? _rulesProxy;

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
        /// Turns the Valve end-match vote off and blanks any slots the client would read as options.
        /// </summary>
        public static void SuppressNativeVote()
        {
            Server.ExecuteCommand("mp_endmatch_votenextmap 0");
            DisableMatchEndRestart();
            BlankFilledNativeSlots();
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

        public static void ForgetRules() => _rulesProxy = null;

        /// <summary>
        /// Writes <see cref="EmptyVoteSlot"/> only into slots that are not already empty.
        /// Caches the game-rules proxy so a tick does not search entities while the pointer is live.
        /// </summary>
        public static bool BlankFilledNativeSlots()
        {
            CCSGameRules? rules = Rules();
            if (rules is null)
            {
                return false;
            }

            bool changed = false;
            if (SlotsNeedBlank(rules.EndMatchMapGroupVoteOptions))
            {
                BlankVoteSlots(rules.EndMatchMapGroupVoteOptions);
                changed = true;
            }

            if (SlotsNeedBlank(rules.EndMatchMapGroupVoteTypes))
            {
                BlankVoteSlots(rules.EndMatchMapGroupVoteTypes);
                changed = true;
            }

            return changed;
        }

        /// <summary>Sets every slot to <see cref="EmptyVoteSlot"/>.</summary>
        internal static void BlankVoteSlots(Span<int> slots) => slots.Fill(EmptyVoteSlot);

        internal static bool SlotsNeedBlank(ReadOnlySpan<int> slots)
        {
            foreach (int slot in slots)
            {
                if (slot != EmptyVoteSlot)
                {
                    return true;
                }
            }

            return false;
        }

        private static CCSGameRules? Rules()
        {
            if (_rulesProxy is { IsValid: true })
            {
                return _rulesProxy.GameRules;
            }

            _rulesProxy = null;
            foreach (CCSGameRulesProxy proxy in Utilities.FindAllEntitiesByDesignerName<CCSGameRulesProxy>("cs_gamerules"))
            {
                _rulesProxy = proxy;
                break;
            }

            return _rulesProxy is { IsValid: true } ? _rulesProxy.GameRules : null;
        }

        public static string ChangelevelCommand(MapOrigin origin, string map) =>
            origin == MapOrigin.Workshop
                ? $"ds_workshop_changelevel {map}"
                : $"changelevel {map}";
    }
}
