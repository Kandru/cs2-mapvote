namespace MapVote.Voting
{
    /// <summary>Pure RTV threshold math, shared by commands and disconnect checks.</summary>
    public static class RtvRule
    {
        /// <summary>Required votes for a live human count, rounded up and clamped to at least one.</summary>
        public static int VotesNeeded(int humanCount, float ratio)
        {
            int humans = Math.Max(1, humanCount);
            return Math.Max(1, (int)Math.Ceiling(humans * ClampRatio(ratio)));
        }

        public static float ClampRatio(float ratio) => Math.Clamp(ratio, 0.01f, 1f);

        /// <summary>
        /// Seconds until the first reminder on a whole interval boundary. With a 60s
        /// window and 15s interval this is 15s (45s left), then 30, 45, and no 0s
        /// reminder. A 0 duration means no deadline and repeats forever.
        /// </summary>
        public static int? NextAnnounceDelay(int durationSeconds, int intervalSeconds)
        {
            if (intervalSeconds <= 0)
            {
                return null;
            }

            if (durationSeconds <= 0)
            {
                return intervalSeconds;
            }

            int remainder = durationSeconds % intervalSeconds;
            int delay = remainder == 0 ? intervalSeconds : remainder;
            return delay < durationSeconds ? delay : null;
        }
    }
}
