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
    /// End-match schedule from the win-panel epoch: fixed match-results hold, then custom menu,
    /// <c>changelevel</c> at <c>mp_endmatch_votenextleveltime</c> − 1s.
    /// </summary>
    public static class VoteClock
    {
        public const int EngineChangelevelSlackSeconds = 1;
        public const int MatchResultsSeconds = 5;
        public const int NativeCommandLeadSeconds = 1;
        public const int DefaultVoteSeconds = 20;

        /// <summary>
        /// Seconds after the win-panel epoch when custom vs native is decided
        /// (<see cref="MatchResultsSeconds"/> − <see cref="NativeCommandLeadSeconds"/>).
        /// </summary>
        public static int DecisionOffsetSeconds => MatchResultsSeconds - NativeCommandLeadSeconds;

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

        /// <summary>RCON catalog retry budget during the match-results hold.</summary>
        public static int CatalogTimeoutSeconds() => DecisionOffsetSeconds;

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
