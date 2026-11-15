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
    }
}
