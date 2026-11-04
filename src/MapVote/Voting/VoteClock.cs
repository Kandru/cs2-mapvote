using MapVote.Config;

namespace MapVote.Voting
{
    /// <param name="OpenSeconds">Voting window length (custom menu, for logs).</param>
    /// <param name="RevealSeconds">Winner-reveal length after the vote locks.</param>
    /// <param name="RevealOffsetSeconds">Seconds after the win-panel epoch when reveal starts.</param>
    /// <param name="ChangelevelOffsetSeconds">
    /// Seconds after the win-panel epoch when the plugin must <c>changelevel</c>
    /// (panel length minus <see cref="VoteClock.EngineChangelevelSlackSeconds"/>).
    /// </param>
    public readonly record struct VoteTiming(
        int OpenSeconds,
        int RevealSeconds,
        int RevealOffsetSeconds,
        int ChangelevelOffsetSeconds);

    /// <summary>
    /// End-match schedule pinned to <c>mp_win_panel_display_time</c>.
    /// The vote length shrinks to fit the catalog query and the winner animation.
    /// </summary>
    public static class VoteClock
    {
        public const int EngineChangelevelSlackSeconds = 1;
        public const int DefaultPanelSeconds = 3;

        /// <summary>How long the Valve end-match vote accepts clicks (<c>endmatch.vote_seconds</c>).</summary>
        public static int NativeVoteSeconds(EndmatchConfig endmatch) =>
            NativeVoteSeconds(endmatch.VoteSeconds);

        public static int NativeVoteSeconds(int voteSeconds) => Math.Max(1, voteSeconds);

        /// <summary>
        /// Catalog retry budget inside the master panel, without eating the reveal or the 1s spare.
        /// </summary>
        public static int QueryBudgetSeconds(int panelSeconds, int catalogWaitSeconds, int winnerRevealSeconds)
        {
            int room = Math.Max(0, panelSeconds)
                - EngineChangelevelSlackSeconds
                - Math.Max(1, winnerRevealSeconds)
                - 1;
            return Math.Min(Math.Max(0, catalogWaitSeconds), Math.Max(0, room));
        }

        /// <summary>
        /// Custom-menu schedule for a win-panel epoch.
        /// <paramref name="queryElapsedSeconds"/> is how long the catalog refresh took.
        /// </summary>
        public static VoteTiming ResolveCustom(
            int panelSeconds,
            int winnerRevealSeconds,
            float queryElapsedSeconds)
        {
            int changelevelAt = Math.Max(0, Math.Max(1, panelSeconds) - EngineChangelevelSlackSeconds);
            float elapsed = Math.Max(0f, queryElapsedSeconds);
            float left = changelevelAt - elapsed;
            if (left < 1f)
            {
                return new VoteTiming(0, 0, changelevelAt, changelevelAt);
            }

            int reveal = (int)Math.Floor(Math.Min(Math.Max(1, winnerRevealSeconds), left));
            int revealAt = changelevelAt - reveal;
            int open = Math.Max(0, (int)Math.Floor(revealAt - elapsed));
            return new VoteTiming(open, reveal, revealAt, changelevelAt);
        }

        /// <summary>
        /// Seconds left until <paramref name="epochUtc"/> + <paramref name="offsetSeconds"/>.
        /// Negative when that deadline has already passed.
        /// </summary>
        public static float SecondsUntil(DateTime epochUtc, int offsetSeconds, DateTime nowUtc) =>
            (float)(epochUtc.AddSeconds(offsetSeconds) - nowUtc).TotalSeconds;

        /// <summary>Reads <c>mp_win_panel_display_time</c>, rounding up so fractional panels still fit.</summary>
        public static int ReadPanelSeconds(Func<string, float?> readFloat, int fallback = DefaultPanelSeconds)
        {
            float? value = readFloat("mp_win_panel_display_time");
            if (value is null || value < 0f)
            {
                return Math.Max(1, fallback);
            }

            return Math.Max(1, (int)Math.Ceiling(value.Value));
        }
    }
}
