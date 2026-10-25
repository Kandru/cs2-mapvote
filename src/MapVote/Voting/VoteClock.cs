using MapVote.Config;

namespace MapVote.Voting
{
    /// <param name="OpenSeconds">Player voting window.</param>
    /// <param name="RevealSeconds">Locked-winner display after voting closes.</param>
    /// <param name="TotalSeconds">Open + reveal (UI lifetime).</param>
    /// <param name="EngineHoldSeconds">
    /// Restart / next-level cvar budget: total + 1s so the plugin can changelevel
    /// before the engine ends the intermission.
    /// </param>
    public readonly record struct VoteTiming(
        int OpenSeconds,
        int RevealSeconds,
        int TotalSeconds,
        int EngineHoldSeconds);

    /// <summary>Splits configured vote and reveal windows into open voting and winner reveal.</summary>
    public static class VoteClock
    {
        public const int EngineChangelevelSlackSeconds = 1;

        public static VoteTiming Resolve(EndmatchConfig endmatch) =>
            Resolve(endmatch.VoteSeconds, endmatch.WinnerRevealSeconds);

        public static VoteTiming Resolve(int voteSeconds, int winnerRevealSeconds)
        {
            int open = Math.Max(1, voteSeconds);
            int reveal = Math.Max(1, winnerRevealSeconds);
            int total = open + reveal;
            return new VoteTiming(open, reveal, total, total + EngineChangelevelSlackSeconds);
        }
    }
}
