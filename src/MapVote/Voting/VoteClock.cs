namespace MapVote.Voting
{
    public readonly record struct VoteTiming(int OpenSeconds, int RevealSeconds, int TotalSeconds);

    /// <summary>Splits end-match display time into open voting and winner reveal.</summary>
    public static class VoteClock
    {
        public static VoteTiming Resolve(float? endmatchVoteNextLevelTime, int? matchRestartDelay, int fallbackVoteSeconds, int revealSeconds)
        {
            int reveal = Math.Max(1, revealSeconds);
            int total = PickTotal(endmatchVoteNextLevelTime, matchRestartDelay, fallbackVoteSeconds, reveal);
            int open = Math.Max(1, total - reveal);
            return new VoteTiming(open, reveal, open + reveal);
        }

        private static int PickTotal(float? endmatch, int? restartDelay, int fallback, int reveal)
        {
            if (endmatch is float e)
            {
                int seconds = (int)Math.Floor(e);
                if (seconds >= reveal + 1)
                {
                    return seconds;
                }
            }

            if (restartDelay is int r && r >= reveal + 1)
            {
                return r;
            }

            int fb = Math.Max(reveal + 1, fallback);
            return fb;
        }
    }
}
