namespace MapVote.Voting
{
    /// <param name="OpenSeconds">Voting window length (custom menu, for logs).</param>
    /// <param name="RevealSeconds">Winner-reveal length after the vote locks.</param>
    /// <param name="RevealOffsetSeconds">Seconds after the win-panel epoch when reveal starts.</param>
    /// <param name="ChangelevelOffsetSeconds">
    /// Seconds after the win-panel epoch when the plugin must <c>changelevel</c>
    /// (vote length minus <see cref="VoteClock.EngineChangelevelSlackSeconds"/>).
    /// The engine cvar is longer: <see cref="VoteClock.EngineHoldFromIntermission"/>.
    /// </param>
    public readonly record struct VoteTiming(
        int OpenSeconds,
        int RevealSeconds,
        int RevealOffsetSeconds,
        int ChangelevelOffsetSeconds);

    /// <summary>
    /// Menu schedule from the win-panel epoch: match-results hold, then the custom menu,
    /// <c>changelevel</c> one second before the vote length elapses.
    /// The engine end-match timer is <see cref="EngineHoldFromIntermission"/>, measured from intermission.
    /// </summary>
    public static class VoteClock
    {
        public const int EngineChangelevelSlackSeconds = 1;

        /// <summary>Win panel arrives about this long after intermission, which is when the engine timer starts.</summary>
        public const int WinPanelDelaySeconds = 3;

        public const int MatchResultsSeconds = 6;
        public const int CatalogBudgetSeconds = 1;
        public const int DecisionOffsetSeconds = 2;
        public const int DefaultVoteSeconds = 20;

        /// <summary>
        /// Custom-menu schedule from the win-panel epoch.
        /// Menu opens after <see cref="MatchResultsSeconds"/>; vote + reveal fit until
        /// <paramref name="voteSeconds"/> − 1.
        /// </summary>
        public static VoteTiming ResolveCustom(int voteSeconds, int winnerRevealSeconds)
        {
            int changelevelAt = Math.Max(0, Math.Max(1, voteSeconds) - EngineChangelevelSlackSeconds);
            int hold = MatchResultsSeconds;
            int phase = changelevelAt - hold;
            if (phase < 1)
            {
                return new VoteTiming(0, 0, changelevelAt, changelevelAt);
            }

            int reveal = Math.Min(Math.Max(1, winnerRevealSeconds), phase);
            int revealAt = changelevelAt - reveal;
            int open = Math.Max(0, revealAt - hold);
            return new VoteTiming(open, reveal, revealAt, changelevelAt);
        }

        /// <summary>
        /// Engine end-match budget measured from intermission.
        /// Our <c>changelevel</c> is <c>voteSeconds − 1</c> after the win panel, so the engine
        /// timer has to cover that delay plus one second of slack.
        /// </summary>
        public static int EngineHoldFromIntermission(int voteSeconds) =>
            Math.Max(1, voteSeconds) + WinPanelDelaySeconds + EngineChangelevelSlackSeconds;

        /// <summary>RCON catalog retry budget, measured from intermission. The path decision follows at <see cref="DecisionOffsetSeconds"/>.</summary>
        public static int CatalogTimeoutSeconds() => CatalogBudgetSeconds;

        /// <summary>
        /// Seconds left until <paramref name="epochUtc"/> + <paramref name="offsetSeconds"/>.
        /// Negative when that deadline has already passed.
        /// </summary>
        public static float SecondsUntil(DateTime epochUtc, int offsetSeconds, DateTime nowUtc) =>
            (float)(epochUtc.AddSeconds(offsetSeconds) - nowUtc).TotalSeconds;

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
