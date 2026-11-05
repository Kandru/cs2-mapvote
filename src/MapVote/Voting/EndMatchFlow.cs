namespace MapVote.Voting
{
    public enum EndMatchPhase
    {
        Idle,
        Preparing,
        Ready,
        Holding,
        Voting,
        Revealing,
        Changing,
        Native,
    }

    /// <summary>Result of a custom-vs-native decision. <see cref="Custom"/> is meaningful only when a decide call accepted it.</summary>
    public readonly record struct PathChoice(bool Custom);

    /// <summary>
    /// End-match phase machine. Intermission prepares a catalog; the path is chosen once;
    /// the win panel only opens our HUD on the custom path.
    /// </summary>
    public sealed class EndMatchFlow
    {
        public EndMatchPhase Phase { get; private set; } = EndMatchPhase.Idle;

        public bool BlocksRtv => Phase != EndMatchPhase.Idle;

        /// <summary>Custom path is committed and the engine may still refill native vote slots.</summary>
        public bool WatchNativeSlots => Phase is EndMatchPhase.Ready
            or EndMatchPhase.Holding
            or EndMatchPhase.Voting
            or EndMatchPhase.Revealing;

        /// <summary>First intermission starts the query. A later one does not restart it.</summary>
        public bool TryBegin()
        {
            if (Phase != EndMatchPhase.Idle)
            {
                return false;
            }

            Phase = EndMatchPhase.Preparing;
            return true;
        }

        /// <summary>
        /// Chooses custom when <paramref name="candidateCount"/> is positive.
        /// False once the decision is already final.
        /// </summary>
        public bool TryDecide(int candidateCount, out PathChoice choice)
        {
            if (Phase != EndMatchPhase.Preparing)
            {
                choice = default;
                return false;
            }

            choice = Decide(candidateCount);
            return true;
        }

        /// <summary>
        /// Win panel. Starts the match-results hold on the custom path.
        /// If the 2s decision has not run yet, decides with <paramref name="candidateCountIfUndecided"/> first.
        /// </summary>
        public bool TryBeginHold(int candidateCountIfUndecided, out bool decidedNow, out PathChoice choice)
        {
            decidedNow = false;
            choice = default;
            switch (Phase)
            {
                case EndMatchPhase.Preparing:
                    choice = Decide(candidateCountIfUndecided);
                    decidedNow = true;
                    if (Phase != EndMatchPhase.Ready)
                    {
                        return false;
                    }

                    Phase = EndMatchPhase.Holding;
                    return true;
                case EndMatchPhase.Ready:
                    Phase = EndMatchPhase.Holding;
                    return true;
                default:
                    return false;
            }
        }

        public bool TryBeginVoting()
        {
            if (Phase != EndMatchPhase.Holding)
            {
                return false;
            }

            Phase = EndMatchPhase.Voting;
            return true;
        }

        public bool TryBeginReveal()
        {
            if (Phase != EndMatchPhase.Voting)
            {
                return false;
            }

            Phase = EndMatchPhase.Revealing;
            return true;
        }

        /// <summary>Voting or reveal may change the map. A second call does not.</summary>
        public bool TryBeginChange()
        {
            if (Phase is not (EndMatchPhase.Voting or EndMatchPhase.Revealing))
            {
                return false;
            }

            Phase = EndMatchPhase.Changing;
            return true;
        }

        /// <summary>
        /// Warmup or a new match on this map. False while our vote is still on screen.
        /// A finished native vote returns to idle so the next intermission can start.
        /// </summary>
        public bool TryFinishForNewMatch()
        {
            switch (Phase)
            {
                case EndMatchPhase.Idle:
                case EndMatchPhase.Native:
                    Phase = EndMatchPhase.Idle;
                    return true;
                default:
                    return false;
            }
        }

        public void Reset() => Phase = EndMatchPhase.Idle;

        private PathChoice Decide(int candidateCount)
        {
            bool custom = candidateCount > 0;
            Phase = custom ? EndMatchPhase.Ready : EndMatchPhase.Native;
            return new PathChoice(custom);
        }
    }
}
