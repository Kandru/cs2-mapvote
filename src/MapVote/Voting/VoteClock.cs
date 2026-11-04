namespace MapVote.Voting
{
    /// <param name="OpenSeconds">Voting window length (custom menu, for logs).</param>
    /// <param name="RevealSeconds">Winner-reveal length after the vote locks.</param>
    /// <param name="RevealOffsetSeconds">Seconds after the win-panel epoch when reveal starts.</param>
    /// <param name="ChangelevelOffsetSeconds">
    /// Seconds after the win-panel epoch when the plugin must <c>changelevel</c>
    /// (<c>mp_endmatch_votenextleveltime</c> minus <see cref="VoteClock.EngineChangelevelSlackSeconds"/>).
    /// </param>
    public readonly record struct VoteTiming(
        int OpenSeconds,
        int RevealSeconds,
        int RevealOffsetSeconds,
        int ChangelevelOffsetSeconds);

    /// <summary>
    /// End-match schedule: menu after <c>mp_win_panel_display_time</c>,
    /// <c>changelevel</c> at <c>mp_endmatch_votenextleveltime</c> − 1s.
    /// </summary>
    public static class VoteClock
    {
        public const int EngineChangelevelSlackSeconds = 1;
        public const int DefaultPanelSeconds = 3;
        public const int DefaultVoteSeconds = 20;
        public const int MinCatalogTimeoutSeconds = 2;

        /// <summary>
        /// Custom-menu schedule from the win-panel epoch.
        /// Menu opens at <paramref name="panelSeconds"/>; vote + reveal fit until
        /// <paramref name="voteSeconds"/> − 1.
        /// </summary>
        public static VoteTiming ResolveCustom(
            int panelSeconds,
            int voteSeconds,
            int winnerRevealSeconds)
        {
            int changelevelAt = Math.Max(0, Math.Max(1, voteSeconds) - EngineChangelevelSlackSeconds);
            int panel = Math.Max(0, panelSeconds);
            int phase = changelevelAt - panel;
            if (phase < 1)
            {
                return new VoteTiming(0, 0, changelevelAt, changelevelAt);
            }

            int reveal = Math.Min(Math.Max(1, winnerRevealSeconds), phase);
            int revealAt = changelevelAt - reveal;
            int open = Math.Max(0, revealAt - panel);
            return new VoteTiming(open, reveal, revealAt, changelevelAt);
        }

        /// <summary>
        /// RCON catalog retry budget during the win panel.
        /// Panel ≤ 0 → 2s; otherwise max(2, panel − 1).
        /// </summary>
        public static int CatalogTimeoutSeconds(int panelSeconds)
        {
            if (panelSeconds <= 0)
            {
                return MinCatalogTimeoutSeconds;
            }

            return Math.Max(MinCatalogTimeoutSeconds, panelSeconds - 1);
        }

        /// <summary>
        /// Seconds left until <paramref name="epochUtc"/> + <paramref name="offsetSeconds"/>.
        /// Negative when that deadline has already passed.
        /// </summary>
        public static float SecondsUntil(DateTime epochUtc, int offsetSeconds, DateTime nowUtc) =>
            (float)(epochUtc.AddSeconds(offsetSeconds) - nowUtc).TotalSeconds;

        /// <summary>
        /// Reads <c>mp_win_panel_display_time</c>, rounding up. 0 is allowed (open immediately).
        /// Missing or negative → fallback.
        /// </summary>
        public static int ReadPanelSeconds(Func<string, float?> readFloat, int fallback = DefaultPanelSeconds)
        {
            float? value = readFloat("mp_win_panel_display_time");
            if (value is null || value < 0f)
            {
                return Math.Max(0, fallback);
            }

            return Math.Max(0, (int)Math.Ceiling(value.Value));
        }

        /// <summary>
        /// Reads <c>mp_endmatch_votenextleveltime</c>, rounding up to at least 1.
        /// Missing or below 1 → fallback.
        /// </summary>
        public static int ReadVoteSeconds(Func<string, float?> readFloat, int fallback = DefaultVoteSeconds)
        {
            float? value = readFloat("mp_endmatch_votenextleveltime");
            if (value is null || value < 1f)
            {
                return Math.Max(1, fallback);
            }

            return Math.Max(1, (int)Math.Ceiling(value.Value));
        }
    }
}
